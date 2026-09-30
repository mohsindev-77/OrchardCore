using Microsoft.Extensions.Localization;
using WorkMate.Platform.Models;

namespace WorkMate.Platform.Services;

/// <summary>
/// The only way to read or change <see cref="WorkMateSettings"/>. Tenant-scoped by
/// construction: nothing here takes a tenant, the shell scope supplies it.
/// </summary>
public interface IWorkMateSettingsService
{
    /// <summary>
    /// The tenant's platform settings, with defaults applied for anything never set and
    /// <see cref="WorkMateSettings.CustomerCode"/> resolved from the tenant name when it has
    /// not been set explicitly. Reading is unrestricted: currency, working week and direction
    /// are needed to render any screen at all.
    /// </summary>
    Task<WorkMateSettings> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates and stores the platform settings. Requires
    /// <see cref="Permissions.ManageWorkMateSettings"/>, checked here rather than only in the
    /// driver so that the API and background jobs are covered too.
    /// </summary>
    Task<SettingsUpdateResult> UpdateAsync(WorkMateSettings settings, CancellationToken cancellationToken = default);
}

/// <summary>What became of a settings write.</summary>
public enum SettingsUpdateStatus
{
    /// <summary>The settings were validated and stored.</summary>
    Updated,

    /// <summary>The acting user does not hold <see cref="Permissions.ManageWorkMateSettings"/>.</summary>
    NotAuthorised,

    /// <summary>The settings failed validation. Nothing was stored.</summary>
    Invalid,
}

/// <summary>The outcome of a settings write, with any validation messages already localised.</summary>
/// <param name="Status">What became of the write.</param>
/// <param name="Errors">
/// One entry per validation failure, keyed by the property that failed so a driver can put the
/// message against the right field.
/// </param>
public sealed record SettingsUpdateResult(
    SettingsUpdateStatus Status,
    IReadOnlyList<SettingsValidationError> Errors)
{
    public static readonly SettingsUpdateResult Updated = new(SettingsUpdateStatus.Updated, []);

    public static readonly SettingsUpdateResult NotAuthorised = new(SettingsUpdateStatus.NotAuthorised, []);

    public static SettingsUpdateResult Invalid(IReadOnlyList<SettingsValidationError> errors) =>
        new(SettingsUpdateStatus.Invalid, errors);

    public bool Succeeded => Status is SettingsUpdateStatus.Updated;
}

/// <param name="PropertyName">The <see cref="WorkMateSettings"/> property at fault.</param>
/// <param name="Message">The localised message to show the user.</param>
public sealed record SettingsValidationError(string PropertyName, LocalizedString Message);
