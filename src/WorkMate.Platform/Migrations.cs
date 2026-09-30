using System.Diagnostics.CodeAnalysis;
using OrchardCore.Data.Migration;

namespace WorkMate.Platform;

/// <summary>
/// Schema for WorkMate.Platform. Migrations are additive: a migration never destroys data,
/// and a deprecation is a new migration that marks and later removes.
/// </summary>
public sealed class Migrations : DataMigration
{
    /// <summary>
    /// The platform owns no tables yet. WorkMateSettings is a site settings section, which
    /// Orchard stores inside the site document, so it needs no schema of its own. This method
    /// exists so that the module has a migration record from its first release and later
    /// versions can be added as UpdateFrom1Async onwards.
    /// </summary>
    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Orchard discovers migration methods by reflection on the DataMigration instance, so CreateAsync cannot be static.")]
    public Task<int> CreateAsync() => Task.FromResult(1);
}
