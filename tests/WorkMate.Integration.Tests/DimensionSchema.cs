using System.Data;
using Microsoft.Extensions.DependencyInjection;
using WorkMate.Dimensions.Internal.Graph;
using WorkMate.Dimensions.Internal.References;
using YesSql;

namespace WorkMate.Integration.Tests;

/// <summary>
/// What <c>WorkMate.Dimensions</c>' schema looks like at each version, for the tests that rewind a
/// tenant to an earlier one.
/// </summary>
/// <remarks>
/// <b>Rolling the recorded version back to N is only half of simulating a tenant at version N.</b>
/// That tenant also does not have the tables the steps above N create, and
/// <c>CreateMapIndexTableAsync</c> fails on one that is already there. When it fails the whole
/// catch-up fails — <em>every</em> module's, because <c>UpdateAllFeaturesAsync</c> is one
/// transaction — which rolls back the document rewrites earlier steps had already made and leaves
/// the shared tenant stuck below head. So the test that forgot it fails, and so does every test in
/// the collection that runs after it, with errors that name neither the step nor the table.
///
/// <b>This is shared rather than private to one test class because three classes rewind the
/// Dimensions version</b> — <c>DimensionsMigrationUpgradeTenantTests</c> deliberately, and
/// <c>DisplayTextRepairTenantTests</c> twice as a means to replaying a data-repair step. When it was
/// private, adding a table-creating step broke the other class in a way that pointed at neither.
/// One list, every reader.
///
/// <b>A new <c>UpdateFromNAsync</c> that creates a table has to be added to
/// <see cref="TablesByVersion"/>.</b> That map is the one place this knowledge lives, so adding a
/// row is the whole of it.
/// </remarks>
internal static class DimensionSchema
{
    /// <summary>The index tables the module creates, and the schema version each first appears at.</summary>
    public static readonly IReadOnlyList<(Type IndexType, int FirstVersion)> TablesByVersion =
    [
        (typeof(DimensionLinkIndex), 3),
        (typeof(DimensionClosureIndex), 3),
        (typeof(EmployeeAssignmentIndex), 3),
        (typeof(UnitHeadIndex), 6),
        (typeof(DimensionReferenceIndex), 13),
    ];

    /// <summary>
    /// Drops every index table a tenant recorded at <paramref name="version"/> would not yet have.
    /// </summary>
    public static async Task DropTablesCreatedAfterAsync(IServiceProvider services, int version)
    {
        var store = services.GetRequiredService<IStore>();
        var connection = store.Configuration.ConnectionFactory.CreateConnection();

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        try
        {
            foreach (var (indexType, _) in TablesByVersion.Where(entry => entry.FirstVersion > version))
            {
                var tableName = store.Configuration.TablePrefix
                    + store.Configuration.TableNameConvention.GetIndexTable(indexType, string.Empty);

                using var command = connection.CreateCommand();
                command.CommandText = $"DROP TABLE IF EXISTS {tableName}";
                await command.ExecuteNonQueryAsync();
            }
        }
        finally
        {
            await connection.CloseAsync();
            await connection.DisposeAsync();
        }
    }
}
