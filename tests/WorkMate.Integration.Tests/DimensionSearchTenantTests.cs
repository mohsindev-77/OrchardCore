using System.Globalization;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using WorkMate.Core;
using WorkMate.Dimensions.Services;
using Xunit;

namespace WorkMate.Integration.Tests;

/// <summary>
/// Search matches both halves of a name, whatever language the reader is in.
/// </summary>
/// <remarks>
/// The decision, taken after the Arabic-optional work raised it: a search term is matched against
/// the English name, the Arabic name and the code, and the UI culture does not narrow that. An
/// Arabic-reading administrator looking for a unit a colleague created in English has to be able to
/// find it by the name that colleague gave it, and the reverse.
///
/// The alternative — match only the reader's own language — looks tidier and is unusable in a
/// half-translated tenant, which is every tenant for its first few months.
/// </remarks>
[Collection(UsesTheBaseTenant.Name)]
public sealed class DimensionSearchTenantTests
{
    private readonly BaseTenantFixture _tenant;

    public DimensionSearchTenantTests(BaseTenantFixture tenant) => _tenant = tenant;

    private static readonly DateOnly Opened = new(2024, 1, 1);

    [Theory]
    [InlineData("en")]
    [InlineData("ar")]
    public async Task AnEnglishNameIsFoundWhateverTheReadersLanguage(string culture) =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var world = await GivenABilingualUnitAsync(services, $"search-en-{culture}");

            var hits = await UnderCultureAsync(culture, () =>
                services.GetRequiredService<IDimensionGraphService>()
                    .SearchAsync(world.StructureId, "Marine Services", Opened));

            hits.Should().Contain(hit => hit.RecordId == world.RecordId);
        });

    [Theory]
    [InlineData("en")]
    [InlineData("ar")]
    public async Task AnArabicNameIsFoundWhateverTheReadersLanguage(string culture) =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var world = await GivenABilingualUnitAsync(services, $"search-ar-{culture}");

            var hits = await UnderCultureAsync(culture, () =>
                services.GetRequiredService<IDimensionGraphService>()
                    .SearchAsync(world.StructureId, "الخدمات البحرية", Opened));

            hits.Should().Contain(hit => hit.RecordId == world.RecordId);
        });

    /// <summary>
    /// A unit with no Arabic name is still findable — by its English name, from the Arabic UI.
    /// </summary>
    /// <remarks>
    /// The honest limit of the decision, written down so it is not mistaken for a defect: an
    /// Arabic <em>term</em> cannot match a record that has no Arabic name, because there is nothing
    /// to match against. What must never happen is the record becoming unfindable altogether, and
    /// this is the assertion that says it has not.
    /// </remarks>
    [Fact]
    public async Task AUnitWithNoArabicNameIsStillFoundByItsEnglishNameUnderArabic() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var world = await GivenABilingualUnitAsync(services, "search-nofallback", arabicName: string.Empty);

            var hits = await UnderCultureAsync("ar", () =>
                services.GetRequiredService<IDimensionGraphService>()
                    .SearchAsync(world.StructureId, "Marine Services", Opened));

            hits.Should().Contain(hit => hit.RecordId == world.RecordId);
        });

    [Fact]
    public async Task TheCodeIsSearchableToo() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var world = await GivenABilingualUnitAsync(services, "search-bycode");

            var hits = await services.GetRequiredService<IDimensionGraphService>()
                .SearchAsync(world.StructureId, "search-bycode-div", Opened);

            hits.Should().Contain(hit => hit.RecordId == world.RecordId);
        });

    // ---- scaffolding --------------------------------------------------------------------

    private sealed record World(string StructureId, string RecordId);

    private static async Task<World> GivenABilingualUnitAsync(
        IServiceProvider services, string prefix, string arabicName = "الخدمات البحرية")
    {
        var types = await DimensionGraphScenario.TypesAsync(services);
        var structureId = await DimensionGraphScenario.StructureAsync(services, $"{prefix}-org");

        var created = await services.GetRequiredService<IDimensionService>().CreateAsync(
            types.Division,
            $"{prefix}-div",
            new BilingualText("Marine Services", arabicName),
            new EffectiveRange(Opened, null));

        created.Succeeded.Should().BeTrue(
            string.Join("; ", created.Errors.Select(error => error.Message.Value)));

        return new World(structureId, created.Value!.RecordId);
    }

    /// <summary>
    /// Runs a read as a reader of <paramref name="culture"/> would.
    /// </summary>
    /// <remarks>
    /// Both cultures are set, not only the UI one: the point of the test is that neither narrows
    /// what search matches, and setting only one would leave the other able to.
    /// </remarks>
    private static async Task<T> UnderCultureAsync<T>(string culture, Func<Task<T>> read)
    {
        var previousUi = CultureInfo.CurrentUICulture;
        var previous = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);

            return await read();
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousUi;
            CultureInfo.CurrentCulture = previous;
        }
    }
}
