using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using OrchardCore.Entities;
using OrchardCore.Environment.Shell;
using OrchardCore.Localization;
using OrchardCore.Settings;
using WorkMate.Platform.Models;

namespace WorkMate.Platform.Services;

/// <inheritdoc />
public sealed partial class WorkMateSettingsService : IWorkMateSettingsService
{
    // Log messages are for operators, not users, so they are not localised. A source-generated
    // LoggerMessage keeps the analyzers happy and the message template in one place.
    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Warning,
        Message = "Tenant '{TenantName}' has default locale '{ConfiguredCulture}' in its WorkMate settings, "
            + "which is not one of its supported cultures ({SupportedCultures}). Falling back to '{FallbackCulture}'. "
            + "Check that the base recipe ran and that this tenant's cultures were not narrowed afterwards.")]
    private partial void LogDefaultCultureFallback(
        string tenantName,
        string configuredCulture,
        string supportedCultures,
        string fallbackCulture);

    private readonly ISiteService _siteService;
    private readonly ILocalizationService _localizationService;
    private readonly IAuthorizationService _authorizationService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ShellSettings _shellSettings;
    private readonly ILogger<WorkMateSettingsService> _logger;
    private readonly IStringLocalizer S;

    public WorkMateSettingsService(
        ISiteService siteService,
        ILocalizationService localizationService,
        IAuthorizationService authorizationService,
        IHttpContextAccessor httpContextAccessor,
        ShellSettings shellSettings,
        ILogger<WorkMateSettingsService> logger,
        IStringLocalizer<WorkMateSettingsService> stringLocalizer)
    {
        _siteService = siteService;
        _localizationService = localizationService;
        _authorizationService = authorizationService;
        _httpContextAccessor = httpContextAccessor;
        _shellSettings = shellSettings;
        _logger = logger;
        S = stringLocalizer;
    }

    public async Task<WorkMateSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var site = await _siteService.GetSiteSettingsAsync();

        // GetOrCreate, not the obsolete As: in 3.0.1 As<T> is marked obsolete in favour of
        // TryGet<T> and GetOrCreate<T>. GetOrCreate hands back an instance carrying this type's
        // property defaults when the tenant has never saved the section.
        var settings = site.GetOrCreate<WorkMateSettings>();

        if (string.IsNullOrWhiteSpace(settings.CustomerCode))
        {
            settings.CustomerCode = DeriveCustomerCode(_shellSettings.Name);
        }

        // The base recipe gives every WorkMate tenant en and ar, so the "en" default is right
        // there. A tenant set up without it — or one whose cultures were narrowed later — would
        // otherwise carry a default locale it is not allowed to save. Fall back to whatever the
        // tenant's own default culture is, so the setting is always valid.
        var supportedCultures = await _localizationService.GetSupportedCulturesAsync();

        if (!supportedCultures.Contains(settings.DefaultCulture, StringComparer.OrdinalIgnoreCase))
        {
            var fallback = await _localizationService.GetDefaultCultureAsync();

            LogDefaultCultureFallback(
                _shellSettings.Name,
                settings.DefaultCulture,
                string.Join(", ", supportedCultures),
                fallback);

            settings.DefaultCulture = fallback;
        }

        return settings;
    }

    public async Task<SettingsUpdateResult> UpdateAsync(
        WorkMateSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        cancellationToken.ThrowIfCancellationRequested();

        var user = _httpContextAccessor.HttpContext?.User;

        if (user is null ||
            !await _authorizationService.AuthorizeAsync(user, Permissions.ManageWorkMateSettings))
        {
            return SettingsUpdateResult.NotAuthorised;
        }

        var errors = await ValidateAsync(settings);

        if (errors.Count > 0)
        {
            return SettingsUpdateResult.Invalid(errors);
        }

        // LoadSiteSettingsAsync returns the uncached document, which is what a write path needs.
        var site = await _siteService.LoadSiteSettingsAsync();
        site.Put(settings);
        await _siteService.UpdateSiteSettingsAsync(site);

        return SettingsUpdateResult.Updated;
    }

    private async Task<IReadOnlyList<SettingsValidationError>> ValidateAsync(WorkMateSettings settings)
    {
        var errors = new List<SettingsValidationError>();

        var supportedCultures = await _localizationService.GetSupportedCulturesAsync();

        if (!supportedCultures.Contains(settings.DefaultCulture, StringComparer.OrdinalIgnoreCase))
        {
            errors.Add(new SettingsValidationError(
                nameof(WorkMateSettings.DefaultCulture),
                S["The default locale must be one of the tenant's supported cultures: {0}.",
                    string.Join(", ", supportedCultures)]));
        }

        if (settings.FiscalYearStartMonth is < 1 or > 12)
        {
            errors.Add(new SettingsValidationError(
                nameof(WorkMateSettings.FiscalYearStartMonth),
                S["The fiscal year must start in a month from 1 to 12."]));
        }
        else
        {
            // A common year, so that a fiscal year cannot be pinned to 29 February.
            var daysInMonth = DateTime.DaysInMonth(CommonYear, settings.FiscalYearStartMonth);

            if (settings.FiscalYearStartDay < 1 || settings.FiscalYearStartDay > daysInMonth)
            {
                errors.Add(new SettingsValidationError(
                    nameof(WorkMateSettings.FiscalYearStartDay),
                    S["The fiscal year must start on a day from 1 to {0} for the month chosen.", daysInMonth]));
            }
        }

        if (!IsIso4217Code(settings.CurrencyCode))
        {
            errors.Add(new SettingsValidationError(
                nameof(WorkMateSettings.CurrencyCode),
                S["The currency must be a three-letter ISO 4217 code, such as AED."]));
        }

        if (settings.WorkingDays is null || settings.WorkingDays.Length == 0)
        {
            errors.Add(new SettingsValidationError(
                nameof(WorkMateSettings.WorkingDays),
                S["At least one day of the week must be a working day."]));
        }
        else if (settings.WorkingDays.Distinct().Count() != settings.WorkingDays.Length)
        {
            errors.Add(new SettingsValidationError(
                nameof(WorkMateSettings.WorkingDays),
                S["A day of the week cannot be listed as a working day more than once."]));
        }

        if (!string.IsNullOrWhiteSpace(settings.CustomerCode) && !IsCustomerCode(settings.CustomerCode))
        {
            errors.Add(new SettingsValidationError(
                nameof(WorkMateSettings.CustomerCode),
                S["The customer code may contain lower-case letters, digits and hyphens only, and must start with a letter."]));
        }

        return errors;
    }

    /// <summary>
    /// Tenant names are <c>&lt;customer-code&gt;-&lt;env&gt;</c> per specification section 3, so
    /// the code is everything before the last hyphen. A tenant whose name has no hyphen — the
    /// default tenant, most obviously — contributes its whole name.
    /// </summary>
    internal static string DeriveCustomerCode(string tenantName)
    {
        if (string.IsNullOrWhiteSpace(tenantName))
        {
            return string.Empty;
        }

        var lastHyphen = tenantName.LastIndexOf('-');

        var code = lastHyphen > 0
            ? tenantName[..lastHyphen]
            : tenantName;

        return code.ToLowerInvariant();
    }

    private const int CommonYear = 2001;

    private static bool IsIso4217Code(string? value) =>
        value is { Length: 3 } && value.All(char.IsAsciiLetterUpper);

    private static bool IsCustomerCode(string value) =>
        char.IsAsciiLetterLower(value[0]) &&
        value.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-');
}
