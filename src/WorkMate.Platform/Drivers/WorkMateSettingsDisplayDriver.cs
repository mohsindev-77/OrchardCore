using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using OrchardCore.DisplayManagement.Entities;
using OrchardCore.DisplayManagement.Handlers;
using OrchardCore.DisplayManagement.Views;
using OrchardCore.Localization;
using OrchardCore.Settings;
using WorkMate.Platform.Models;
using WorkMate.Platform.Services;
using WorkMate.Platform.ViewModels;

namespace WorkMate.Platform.Drivers;

/// <summary>
/// Renders and updates the platform settings editor. It shapes data for the view and
/// delegates every decision to <see cref="IWorkMateSettingsService"/>; no business logic here.
/// </summary>
public sealed class WorkMateSettingsDisplayDriver : SiteDisplayDriver<WorkMateSettings>
{
    /// <summary>The settings group this editor appears under, at Admin/Settings/workmate.</summary>
    public const string GroupId = "workmate";

    private readonly IWorkMateSettingsService _settingsService;
    private readonly ILocalizationService _localizationService;
    private readonly IAuthorizationService _authorizationService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public WorkMateSettingsDisplayDriver(
        IWorkMateSettingsService settingsService,
        ILocalizationService localizationService,
        IAuthorizationService authorizationService,
        IHttpContextAccessor httpContextAccessor)
    {
        _settingsService = settingsService;
        _localizationService = localizationService;
        _authorizationService = authorizationService;
        _httpContextAccessor = httpContextAccessor;
    }

    protected override string SettingsGroupId => GroupId;

    public override async Task<IDisplayResult?> EditAsync(
        ISite site,
        WorkMateSettings settings,
        BuildEditorContext context)
    {
        if (!await IsAuthorisedAsync())
        {
            return null;
        }

        // Read through the service, so the editor shows the same resolved customer code that
        // every other caller sees rather than the raw stored value.
        var resolved = await _settingsService.GetAsync();
        var supportedCultures = await _localizationService.GetSupportedCulturesAsync();

        return Initialize<WorkMateSettingsViewModel>(
                "WorkMateSettings_Edit",
                model => WorkMateSettingsViewModel.Fill(model, resolved, [.. supportedCultures]))
            .Location("Content:1")
            .OnGroup(GroupId);
    }

    public override async Task<IDisplayResult?> UpdateAsync(
        ISite site,
        WorkMateSettings settings,
        UpdateEditorContext context)
    {
        if (!await IsAuthorisedAsync())
        {
            return null;
        }

        var model = new WorkMateSettingsViewModel();
        await context.Updater.TryUpdateModelAsync(model, Prefix);

        var candidate = model.ToSettings();
        var result = await _settingsService.UpdateAsync(candidate);

        if (result.Succeeded)
        {
            // Orchard writes the section instance it handed us back into the site document
            // after this returns. The service has already stored the same values; copying them
            // onto that instance keeps the two writes identical instead of racing.
            candidate.CopyTo(settings);
        }

        foreach (var error in result.Errors)
        {
            context.Updater.ModelState.AddModelError(
                $"{Prefix}.{error.PropertyName}",
                error.Message.Value);
        }

        return await EditAsync(site, settings, context);
    }

    private async Task<bool> IsAuthorisedAsync()
    {
        var user = _httpContextAccessor.HttpContext?.User;

        return user is not null &&
            await _authorizationService.AuthorizeAsync(user, Permissions.ManageWorkMateSettings);
    }
}
