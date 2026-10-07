using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OrchardCore.Deployment;
using WorkMate.Core;
using WorkMate.Dimensions.Deployment;
using WorkMate.Dimensions.Models;
using WorkMate.Dimensions.Services;
using WorkMate.Platform.Services;
using Xunit;

namespace WorkMate.Integration.Tests;

/// <summary>
/// ADR-0011's real proof: seed a tenant, export it, import the export into a brand-new tenant, and
/// assert the two describe the same organisation.
/// </summary>
/// <remarks>
/// This is the test the export exists to make possible, and the one that says whether the importers
/// are complete. Reading the exporter and the importers side by side cannot: a field the exporter
/// forgets looks exactly like a field the importer does not need, and both halves can be
/// individually reasonable while the pair loses data.
///
/// The seeded tenant is built to contain every shape the engine has that an export could flatten:
/// a substantive rename with history either side of it, a backdated move that splits rather than
/// overwrites, a retirement, a unit taken off the tree, a self-nested unit, and a ragged tree where
/// one type is reachable at two depths. Equality is asserted on the <em>closure</em> at dates
/// before and after each change, not only on today's tree — a flattened history produces an
/// identical tree today and a different answer about last March, which is the whole point of a
/// dated engine.
/// </remarks>
public sealed class DimensionExportRoundTripTenantTests : IAsyncLifetime, IDisposable
{
    /// <summary>
    /// Both hosts are WebApplicationFactory instances, which are disposable. DisposeAsync below is
    /// what actually releases them; this exists because the analyser cannot see that an
    /// IAsyncLifetime does the job, and disposing twice is harmless.
    /// </summary>
    public void Dispose()
    {
        _source?.Dispose();
        _destination?.Dispose();
    }

    private BaseTenantFixture _source = default!;
    private BaseTenantFixture _destination = default!;

    // The dates the seeded history turns on, and the dates the closure is compared on.
    private static readonly DateOnly Opened = new(2024, 1, 1);
    private static readonly DateOnly Renamed = new(2024, 4, 1);
    private static readonly DateOnly Moved = new(2024, 7, 1);
    private static readonly DateOnly Backdated = new(2024, 5, 1);
    private static readonly DateOnly LeftTheTree = new(2024, 9, 1);
    private static readonly DateOnly Retired = new(2024, 11, 1);

    public async Task InitializeAsync()
    {
        // Two hosts, each with its own content root under the temp directory, so the destination is
        // a genuinely empty tenant rather than the source with things added to it.
        _source = new BaseTenantFixture();
        _destination = new BaseTenantFixture();

        await _source.InitializeAsync();
        await _destination.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        await _source.DisposeAsync();
        await _destination.DisposeAsync();
    }

    [Fact]
    public async Task AnExportedTenantImportsIntoAFreshOneAndDescribesTheSameOrganisation()
    {
        await SeedAsync(_source);

        // The destination is empty of everything this test is about to assert equality on. Without
        // this the test would still pass if both fixtures resolved to the same tenant, which is the
        // one way a round-trip test can be comprehensively green and prove nothing at all.
        (await DescribeAsync(_destination)).Types.Should().BeEmpty(
            "the destination must be a genuinely fresh tenant, not the source under another name");

        var recipe = await ExportAsync(_source);

        recipe["steps"]!.AsArray().Should().HaveCount(3, "types, structures and records, in that order");

        // The export is a recipe, so importing it is running a recipe — the same path an operator
        // takes with the downloaded file, and the same importers every other recipe goes through.
        await ApplyAsync(_destination, recipe);

        await AssertSameAsync();
    }

    // ---- the seeded organisation ---------------------------------------------------------

    /// <summary>
    /// A ragged, self-nesting structure with every kind of dated history on it.
    /// </summary>
    private static async Task SeedAsync(BaseTenantFixture tenant) =>
        await InTenantAsync(tenant, async services =>
        {
            var types = services.GetRequiredService<IDimensionTypeService>();
            var structures = services.GetRequiredService<IStructureService>();
            var records = services.GetRequiredService<IDimensionService>();
            var graph = services.GetRequiredService<IDimensionGraphService>();

            async Task<string> TypeAsync(string code, bool selfNesting, params DimensionAttributeDefinition[] schema)
            {
                var created = await types.CreateAsync(
                    code, new BilingualText(code, $"{code}-ar"), schema, selfNesting);

                created.Succeeded.Should().BeTrue(Why(created.Errors));

                return created.Value!.DimensionTypeId;
            }

            // A type with an attribute schema, which ADR-0011 made exportable.
            var division = await TypeAsync("rt-division", selfNesting: false);
            var region = await TypeAsync("rt-region", selfNesting: false);
            var branch = await TypeAsync(
                "rt-branch",
                selfNesting: false,
                new DimensionAttributeDefinition("BranchCode", new BilingualText("Branch code", "رمز الفرع"), DimensionAttributeKind.Text, IsRequired: true),
                new DimensionAttributeDefinition("OpenedOn", new BilingualText("Opened on", "تاريخ الافتتاح"), DimensionAttributeKind.Date));

            // Self-nesting, so a Department sits inside a Department.
            var department = await TypeAsync("rt-department", selfNesting: true);

            // Ragged: a Division holds Departments directly or Regions; a Region holds Branches;
            // a Branch holds Departments again, three levels further down.
            var structure = await structures.CreateAsync(
                "rt-org",
                new BilingualText("Round Trip Org", "هيكل الاختبار"),
                [division, region, branch, department],
                allowSkipLevel: false,
                isStrict: true,
                isPrimaryOrganisation: false,
                new StructureShape(
                    [division],
                    [
                        new StructureContainment(division, department),
                        new StructureContainment(division, region),
                        new StructureContainment(region, branch),
                        new StructureContainment(branch, department),
                        new StructureContainment(department, department),
                    ]));

            structure.Succeeded.Should().BeTrue(Why(structure.Errors));

            var structureId = structure.Value!.StructureId;

            async Task<string> RecordAsync(string code, string typeId)
            {
                var created = await records.CreateAsync(
                    typeId, code, new BilingualText(code, $"{code}-ar"), new EffectiveRange(Opened, null));

                created.Succeeded.Should().BeTrue(Why(created.Errors));

                return created.Value!.RecordId;
            }

            var headOffice = await RecordAsync("rt-head", division);
            var network = await RecordAsync("rt-network", division);
            var credit = await RecordAsync("rt-credit", department);
            var punjab = await RecordAsync("rt-punjab", region);
            var lahore = await RecordAsync("rt-lahore", branch);
            var lahoreCredit = await RecordAsync("rt-lahore-credit", department);
            var nested = await RecordAsync("rt-nested", department);
            var leaving = await RecordAsync("rt-leaving", department);
            var doomed = await RecordAsync("rt-doomed", department);

            async Task MoveAsync(string child, string? parent, DateOnly on)
            {
                var moved = await graph.MoveAsync(structureId, child, parent, on);
                moved.Succeeded.Should().BeTrue(Why(moved.Errors));
            }

            await MoveAsync(credit, headOffice, Opened);
            await MoveAsync(punjab, network, Opened);
            await MoveAsync(lahore, punjab, Opened);
            await MoveAsync(lahoreCredit, lahore, Opened);

            // Self-nested: a Department inside a Department.
            await MoveAsync(nested, credit, Opened);

            // A move, then a backdated one inside it. ADR-0005's addendum: the backdated move
            // claims only up to the day before the one already on record, so this produces three
            // periods rather than overwriting one — exactly the shape a flattened export loses.
            await MoveAsync(leaving, headOffice, Opened);
            await MoveAsync(leaving, network, Moved);
            await MoveAsync(leaving, credit, Backdated);

            // Taken off the tree: a dated entry naming no parent, per stage D2.
            await MoveAsync(leaving, null, LeftTheTree);

            // A substantive rename, so earlier periods keep the old name.
            var renamed = await records.RenameAsync(
                headOffice, new BilingualText("Head Office Renamed", "المركز الرئيسي"), Renamed);

            renamed.Succeeded.Should().BeTrue(Why(renamed.Errors));

            // A retirement, so the record carries an end date and still resolves before it.
            await MoveAsync(doomed, headOffice, Opened);

            var retired = await records.RetireAsync(doomed, Retired);

            retired.Succeeded.Should().BeTrue(Why(retired.Errors));
        });

    // ---- export and import ----------------------------------------------------------------

    private static async Task<JsonObject> ExportAsync(BaseTenantFixture tenant)
    {
        JsonObject? recipe = null;

        await InTenantAsync(tenant, async services =>
        {
            // The same source the deployment plan runs, driven directly: a plan would add a file
            // builder and a download, neither of which is what is under test here.
            var source = services.GetServices<IDeploymentSource>()
                .Single(candidate => candidate.GetType() == typeof(DimensionsDeploymentSource));

            var result = new DeploymentPlanResult(
                new MemoryFileBuilder(), new OrchardCore.Recipes.Models.RecipeDescriptor());

            await source.ProcessDeploymentStepAsync(new DimensionsDeploymentStep(), result);
            await result.FinalizeAsync();

            recipe = result.Recipe;
        });

        recipe.Should().NotBeNull();

        return recipe!;
    }

    private static async Task ApplyAsync(BaseTenantFixture tenant, JsonObject recipe)
    {
        await InTenantAsync(tenant, async services =>
        {
            var directory = Directory.CreateTempSubdirectory("workmate-roundtrip-");

            try
            {
                await File.WriteAllTextAsync(
                    Path.Combine(directory.FullName, "recipe.json"), recipe.ToJsonString());

                var fileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(directory.FullName);

                var descriptor = new OrchardCore.Recipes.Models.RecipeDescriptor
                {
                    Name = "round-trip",
                    BasePath = string.Empty,
                    FileProvider = fileProvider,
                    RecipeFileInfo = fileProvider.GetFileInfo("recipe.json"),
                    RequireNewScope = false,
                };

                await services.GetRequiredService<OrchardCore.Recipes.Services.IRecipeExecutor>()
                    .ExecuteAsync(
                        Guid.NewGuid().ToString("n"),
                        descriptor,
                        new Dictionary<string, object>(),
                        CancellationToken.None);
            }
            finally
            {
                directory.Delete(recursive: true);
            }
        });
    }

    // ---- equality -------------------------------------------------------------------------

    private async Task AssertSameAsync()
    {
        var before = await DescribeAsync(_source);
        var after = await DescribeAsync(_destination);

        after.Types.Should().BeEquivalentTo(before.Types, "every type and its attribute schema travels");
        after.Structures.Should().BeEquivalentTo(before.Structures, "levels, root types, containment and flags travel");
        after.Records.Should().BeEquivalentTo(before.Records, "every unit, its dates and its name travel");

        // The closure, on a date either side of every change the seed made. This is the assertion
        // a flattened history fails and a tree comparison passes.
        after.Closure.Should().BeEquivalentTo(before.Closure, "the dated graph is the same on every date, not only today");

        // And the names as at a date, which is what a substantive rename is for.
        after.NamesOnDates.Should().BeEquivalentTo(before.NamesOnDates, "a rename keeps its history");
    }

    private sealed record Snapshot(
        IReadOnlyList<string> Types,
        IReadOnlyList<string> Structures,
        IReadOnlyList<string> Records,
        IReadOnlyList<string> Closure,
        IReadOnlyList<string> NamesOnDates);

    /// <summary>
    /// Everything about a tenant's organisation, as comparable strings.
    /// </summary>
    /// <remarks>
    /// Strings rather than objects, and codes rather than ids: the two tenants generate their own
    /// ids, so an id comparison would fail on every row for a reason that has nothing to do with
    /// whether the export was faithful. A failing string also reads as the fact it is about.
    /// </remarks>
    private static async Task<Snapshot> DescribeAsync(BaseTenantFixture tenant)
    {
        var types = new List<string>();
        var structures = new List<string>();
        var records = new List<string>();
        var closure = new List<string>();
        var names = new List<string>();

        await InTenantAsync(tenant, async services =>
        {
            var typeService = services.GetRequiredService<IDimensionTypeService>();
            var structureService = services.GetRequiredService<IStructureService>();
            var recordService = services.GetRequiredService<IDimensionService>();
            var graph = services.GetRequiredService<IDimensionGraphService>();

            var allTypes = (await typeService.ListAsync(includeRetired: true))
                .Where(type => type.Code.StartsWith("rt-", StringComparison.Ordinal))
                .OrderBy(type => type.Code, StringComparer.Ordinal)
                .ToList();

            var typeCodeById = allTypes.ToDictionary(type => type.DimensionTypeId, type => type.Code, StringComparer.Ordinal);

            foreach (var type in allTypes)
            {
                var schema = string.Join(", ", type.AttributeSchema.Select(attribute =>
                    $"{attribute.Name}:{attribute.Kind}:{attribute.IsRequired}:{attribute.Label.En}/{attribute.Label.Ar}"));

                types.Add($"{type.Code} | {type.Name.En} | {type.Name.Ar} | selfNesting={type.AllowsSelfNesting} | [{schema}]");
            }

            // Null before the import, which is the state the freshness guard asserts. Describing an
            // empty tenant has to be possible for "this tenant is empty" to be sayable at all.
            var structure = (await structureService.ListAsync())
                .FirstOrDefault(candidate => candidate.Code == "rt-org");

            if (structure is null)
            {
                return;
            }


            var containment = string.Join(", ", structure.Containment
                .Select(pair => $"{typeCodeById.GetValueOrDefault(pair.ParentDimensionTypeId, "?")}>{typeCodeById.GetValueOrDefault(pair.ChildDimensionTypeId, "?")}")
                .OrderBy(pair => pair, StringComparer.Ordinal));

            var levels = string.Join(", ", structure.DimensionTypeIds.Select(id => typeCodeById.GetValueOrDefault(id, "?")));
            var roots = string.Join(", ", structure.RootDimensionTypeIds.Select(id => typeCodeById.GetValueOrDefault(id, "?")).OrderBy(code => code, StringComparer.Ordinal));

            structures.Add($"{structure.Code} | {structure.Name.En} | levels=[{levels}] | roots=[{roots}] | containment=[{containment}] | strict={structure.IsStrict}");

            var allRecords = new List<DimensionNodeRef>();

            foreach (var type in allTypes)
            {
                allRecords.AddRange(await recordService.ListByTypeAsync(type.DimensionTypeId));
            }

            allRecords = [.. allRecords.OrderBy(record => record.Code, StringComparer.Ordinal)];

            var codeById = allRecords.ToDictionary(record => record.RecordId, record => record.Code, StringComparer.Ordinal);

            foreach (var record in allRecords)
            {
                records.Add(
                    $"{record.Code} | {typeCodeById.GetValueOrDefault(record.DimensionTypeId, "?")} | {record.NameEn} | {record.NameAr} | "
                    + $"{record.EffectiveRange.From:yyyy-MM-dd}..{record.EffectiveRange.To:yyyy-MM-dd}");
            }

            // Every date the seed turns on, and a day either side of each, so a period that is one
            // day out shows up as a difference rather than hiding between two samples.
            var dates = new[] { Opened, Renamed, Backdated, Moved, LeftTheTree, Retired }
                .SelectMany(date => new[] { date.AddDays(-1), date })
                .Distinct()
                .OrderBy(date => date)
                .ToList();

            foreach (var date in dates)
            {
                foreach (var record in allRecords)
                {
                    var ancestors = await graph.GetAncestorsAsync(structure.StructureId, record.RecordId, date);

                    var chain = string.Join(
                        " > ",
                        ancestors
                            .OrderByDescending(ancestor => ancestor.Depth)
                            .Select(ancestor => codeById.GetValueOrDefault(ancestor.RecordId, "?")));

                    closure.Add($"{date:yyyy-MM-dd} | {record.Code} | {chain}");

                    var asAt = await recordService.GetAsync(record.RecordId, date);

                    names.Add($"{date:yyyy-MM-dd} | {record.Code} | {asAt?.NameEn ?? "(gone)"}");
                }
            }
        });

        return new Snapshot(types, structures, records, closure, names);
    }

    private static Task InTenantAsync(BaseTenantFixture tenant, Func<IServiceProvider, Task> work) =>
        tenant.InTenantAsync(async services =>
        {
            using (services.GetRequiredService<ISystemOperation>().Begin("round trip test"))
            {
                await work(services);
            }
        });

    private static string Why(IReadOnlyList<DimensionError> errors) =>
        string.Join("; ", errors.Select(error => error.Message.Value));

    /// <summary>
    /// A file builder that keeps nothing: the recipe is read from
    /// <see cref="DeploymentPlanResult.Recipe"/>, not from a file on disk.
    /// </summary>
    private sealed class MemoryFileBuilder : IFileBuilder
    {
        public Task SetFileAsync(string subpath, Stream stream) => Task.CompletedTask;
    }
}
