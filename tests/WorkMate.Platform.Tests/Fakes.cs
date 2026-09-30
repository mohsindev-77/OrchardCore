using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Localization;
using OrchardCore.Localization;
using OrchardCore.Settings;

namespace WorkMate.Platform.Tests;

/// <summary>
/// Holds one <see cref="SiteSettings"/> document in memory. Using Orchard's own concrete
/// <see cref="ISite"/> rather than a hand-written double keeps the tests honest about how
/// settings sections are actually serialised.
/// </summary>
internal sealed class FakeSiteService : ISiteService
{
    public SiteSettings Site { get; } = new();

    public int UpdateCount { get; private set; }

    public Task<ISite> GetSiteSettingsAsync() => Task.FromResult<ISite>(Site);

    public Task<ISite> LoadSiteSettingsAsync() => Task.FromResult<ISite>(Site);

    public Task UpdateSiteSettingsAsync(ISite site)
    {
        UpdateCount++;
        return Task.CompletedTask;
    }
}

internal sealed class FakeLocalizationService : ILocalizationService
{
    public string[] SupportedCultures { get; set; } = ["en", "ar"];

    public string DefaultCulture { get; set; } = "en";

    public bool FallBackToParentCultures => true;

    public Task<string> GetDefaultCultureAsync() => Task.FromResult(DefaultCulture);

    public Task<string[]> GetSupportedCulturesAsync() => Task.FromResult(SupportedCultures);

    public IEnumerable<System.Globalization.CultureInfo> GetAllCulturesAndAliases() =>
        SupportedCultures.Select(System.Globalization.CultureInfo.GetCultureInfo);
}

internal sealed class FakeAuthorizationService : IAuthorizationService
{
    public bool Allow { get; set; } = true;

    public Task<AuthorizationResult> AuthorizeAsync(
        ClaimsPrincipal user,
        object? resource,
        IEnumerable<IAuthorizationRequirement> requirements) =>
        Task.FromResult(Allow ? AuthorizationResult.Success() : AuthorizationResult.Failed());

    public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName) =>
        Task.FromResult(Allow ? AuthorizationResult.Success() : AuthorizationResult.Failed());
}

/// <summary>
/// Returns the msgid unchanged. The tests assert on behaviour, not on translations; whether a
/// string has a translation is the localisation gate's job, not this one's.
/// </summary>
internal sealed class PassThroughStringLocalizer<T> : IStringLocalizer<T>
{
    public LocalizedString this[string name] => new(name, name, resourceNotFound: false);

    public LocalizedString this[string name, params object[] arguments] =>
        new(name, string.Format(System.Globalization.CultureInfo.InvariantCulture, name, arguments), resourceNotFound: false);

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
}
