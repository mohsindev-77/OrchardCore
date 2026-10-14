using System.Globalization;
using FluentAssertions;
using WorkMate.Core;
using WorkMate.Dimensions.Internal;
using WorkMate.Dimensions.Models;
using WorkMate.Dimensions.Services;
using Xunit;

namespace WorkMate.Dimensions.Tests;

/// <summary>
/// How a tenant's dimension types become Organisation menu entries — decision D23, ADR-0024.
/// </summary>
/// <remarks>
/// <b>Every rule here is a product decision, so every rule here is a test.</b> How types are
/// grouped, what the entry reads, how many values it lists, when a value names its organisation:
/// each is something a product owner will want to change, and each is one line to read rather than
/// a tenant, a shell and four recipes to set up.
///
/// The scenario the grouping exists for is the one that got the per-dimension menu withdrawn: three
/// demo organisations in one tenant, each declaring its own Department and Division types, giving a
/// menu that read <em>Departments, Departments, Departments</em>. Decision D17 proposed sharing the
/// types and that was built and reverted — a shared type is a level of every chart that declares it,
/// so each company's chart grew the others' units. D23 groups them in the menu instead.
/// </remarks>
public sealed class DimensionMenuCompositionTests
{
    [Fact]
    public void TypesSharingAnEnglishNameBecomeOneEntry()
    {
        var menu = Compose(
            Type("zenith-department", "Department", "إدارة", "Departments", "الإدارات", order: 20),
            Type("crescent-department", "Department", "إدارة", "Departments", "الإدارات", order: 70),
            Type("demo-department", "Department", "إدارة", "Departments", "الإدارات", order: 120));

        menu.Groups.Should().HaveCount(1, "three Department types are one dimension called Department");

        menu.Groups[0].Caption(English).Should().Be("Departments");
        menu.Groups[0].DimensionTypeIds.Should().HaveCount(3);
    }

    /// <summary>
    /// The grouping ignores case, which is what D23 asks for in so many words.
    /// </summary>
    /// <remarks>
    /// Reachable without anybody doing anything odd: two recipes written by two people, one saying
    /// <em>Cost centre</em> and one <em>Cost Centre</em>, is a tenant with two entries that read
    /// almost the same — which is the complaint this exists to answer.
    /// </remarks>
    [Fact]
    public void TheGroupingIgnoresCaseAndSurroundingSpace()
    {
        var menu = Compose(
            Type("a", "Cost centre", "مركز التكلفة", "Cost centres", "مراكز التكلفة", order: 10),
            Type("b", "  cost CENTRE  ", "مركز التكلفة", "", "", order: 20));

        menu.Groups.Should().HaveCount(1);
        menu.Groups[0].Caption(English).Should().Be("Cost centres", "the authored plural, from the first member");
    }

    /// <summary>
    /// A group with no authored plural anywhere reads as its singular. Nothing is derived.
    /// </summary>
    /// <remarks>
    /// <b>The whole of ADR-0024 in one assertion.</b> The withdrawn menu appended an "s" and
    /// produced <em>Branchs</em>; the fallback is wrong-looking and never wrong, which is the trade
    /// taken deliberately over inventing a word.
    /// </remarks>
    [Fact]
    public void NoAuthoredPluralAnywhereReadsAsTheSingular()
    {
        var menu = Compose(Type("branch", "Branch", "فرع", "", "", order: 10));

        menu.Groups[0].Caption(English).Should().Be("Branch");
        menu.Groups[0].Caption(Arabic).Should().Be("فرع");
    }

    /// <summary>
    /// The first <em>authored</em> plural wins, not the first member's blank one.
    /// </summary>
    /// <remarks>
    /// Types sharing an entry should carry identical plurals and the recipes ship them that way. If
    /// one was left blank, the useful answer is the one somebody did author rather than the blank
    /// that happened to sort first — which would drop the whole group back to its singular.
    /// </remarks>
    [Fact]
    public void AGroupTakesTheFirstAuthoredPluralRatherThanTheFirstMembers()
    {
        var menu = Compose(
            Type("first", "Region", "منطقة", "", "", order: 10),
            Type("second", "Region", "منطقة", "Regions", "المناطق", order: 20));

        menu.Groups[0].Caption(English).Should().Be("Regions");
    }

    /// <summary>
    /// A group sorts where its lowest-numbered member would, and the order is the same in both
    /// languages.
    /// </summary>
    /// <remarks>
    /// <b>The defect this exists for.</b> The withdrawn menu ordered by caption — through
    /// <c>LocalizedString.PrefixPosition()</c> — so an Arabic reader got an alphabetically Arabic
    /// menu and an English reader an alphabetically English one: two different menus for one tenant,
    /// for an order that is a product decision rather than an accident of spelling.
    /// </remarks>
    [Fact]
    public void GroupsSortByTheirLowestMenuOrderAndTheOrderDoesNotDependOnTheReader()
    {
        var types = new[]
        {
            Type("cost-centre", "Cost centre", "مركز التكلفة", "Cost centres", "مراكز التكلفة", order: 90),
            Type("zenith-department", "Department", "إدارة", "Departments", "الإدارات", order: 20),
            Type("crescent-department", "Department", "إدارة", "Departments", "الإدارات", order: 70),
            Type("zenith-division", "Division", "قطاع", "Divisions", "القطاعات", order: 10),
        };

        var menu = Compose(types);

        menu.Groups.Select(group => group.GroupKey).Should().Equal(
            ["division", "department", "cost centre"],
            "Division 10, Department min(20, 70), Cost centre 90");

        // Built once and read twice: the order is a property of the data, so it cannot differ
        // between two readers of the same cached menu.
        menu.Groups.Select(group => group.Caption(Arabic)).Should().Equal(
            ["القطاعات", "الإدارات", "مراكز التكلفة"]);
    }

    /// <summary>
    /// Hidden and retired types get no entry, and take their values with them.
    /// </summary>
    [Fact]
    public void HiddenAndRetiredDimensionsAreNotInTheMenu()
    {
        var hidden = Type("hidden", "Project", "مشروع", "Projects", "المشاريع", order: 10);
        hidden.ShowInMenu = false;

        var retired = Type("retired", "Team", "فريق", "Teams", "الفرق", order: 20);
        retired.RetiredOn = new DateOnly(2025, 1, 1);

        var shown = Type("shown", "Branch", "فرع", "Branches", "الفروع", order: 30);

        Compose(hidden, retired, shown).Groups.Select(group => group.GroupKey)
            .Should().Equal(["branch"]);
    }

    /// <summary>
    /// A value names its organisation only when the entry covers more than one chart.
    /// </summary>
    /// <remarks>
    /// <b>Asked of the group, not of the type.</b> One Department type on one chart needs no
    /// bracket; three Department types on three charts need one on every value, including the ones
    /// whose own type is unambiguous — otherwise two of the three read plainly and the reader has no
    /// way to tell which company they belong to.
    /// </remarks>
    [Fact]
    public void AValueNamesItsOrganisationOnlyWhenTheEntrySpansMoreThanOne()
    {
        var zenith = Type("zenith-department", "Department", "إدارة", "Departments", "الإدارات", order: 20);
        var crescent = Type("crescent-department", "Department", "إدارة", "Departments", "الإدارات", order: 70);

        var alone = Compose(
            [zenith],
            Structures((zenith, "zenith-org", "Zenith Engineering & Construction", "زينيث")),
            Records((zenith, "civil", "Civil", "الهندسة المدنية")));

        alone.Groups[0].EnglishOrder[0].Caption(English).Should().Be("Civil", "one chart, nothing to disambiguate");

        var together = Compose(
            [zenith, crescent],
            Structures(
                (zenith, "zenith-org", "Zenith Engineering & Construction", "زينيث"),
                (crescent, "crescent-org", "Crescent Microfinance", "كريسنت")),
            Records(
                (zenith, "civil", "Civil", "الهندسة المدنية"),
                (crescent, "credit", "Credit", "الائتمان")));

        together.Groups[0].ShowsOrganisation.Should().BeTrue();

        together.Groups[0].EnglishOrder.Select(value => value.Caption(English)).Should().Equal(
            ["Civil (Zenith Engineering & Construction)", "Credit (Crescent Microfinance)"]);

        together.Groups[0].EnglishOrder[0].Caption(Arabic).Should().Be("الهندسة المدنية (زينيث)");
    }

    /// <summary>
    /// The values are alphabetical in the reader's own language, and that is two different lists.
    /// </summary>
    /// <remarks>
    /// <b>Both orders are computed when the menu is built and both are kept</b>, because the menu is
    /// cached per tenant and serves both readers. Sorting at render time would be the honest
    /// alternative and would mean caching every value of every dimension rather than fifteen; taking
    /// fifteen by English spelling and rendering them to an Arabic reader would be neither.
    /// </remarks>
    [Fact]
    public void TheValuesAreAlphabeticalInTheReadersOwnLanguage()
    {
        var type = Type("department", "Department", "إدارة", "Departments", "الإدارات", order: 10);

        var menu = Compose(
            [type],
            Structures((type, "org", "Org", "منظمة")),
            Records(
                (type, "zulu", "Zulu", "ألف"),
                (type, "alpha", "Alpha", "ياء")));

        menu.Groups[0].EnglishOrder.Select(value => value.Name.En).Should().Equal(["Alpha", "Zulu"]);
        menu.Groups[0].ArabicOrder.Select(value => value.Name.En).Should().Equal(["Zulu", "Alpha"]);
    }

    /// <summary>
    /// Fifteen values, then the menu stops and says how many there are.
    /// </summary>
    /// <remarks>
    /// <b>Sorted, then cut.</b> The fifteen a reader sees are the first fifteen of the dimension, so
    /// <em>Show all</em> means "and the rest after these" rather than "and fifteen arbitrary others
    /// as well". The count reported is the whole dimension, which is what the list screen will
    /// contain and therefore the number a reader is deciding whether to open.
    /// </remarks>
    [Fact]
    public void AtMostFifteenValuesAreListedAndTheRestAreCounted()
    {
        var type = Type("department", "Department", "إدارة", "Departments", "الإدارات", order: 10);

        var records = Enumerable.Range(1, 20)
            .Select(number => (type, $"d{number:00}", $"Department {number:00}", $"إدارة {number:00}"))
            .ToArray();

        var menu = Compose([type], Structures((type, "org", "Org", "منظمة")), Records(records));

        menu.Groups[0].TotalValues.Should().Be(20);
        menu.Groups[0].HasMore.Should().BeTrue();
        menu.Groups[0].EnglishOrder.Should().HaveCount(DimensionMenuGroup.MaxValues);

        menu.Groups[0].EnglishOrder.Select(value => value.Name.En).Should().Equal(
            Enumerable.Range(1, 15).Select(number => $"Department {number:00}"),
            "the first fifteen of the dimension, not fifteen of them");
    }

    /// <summary>A dimension with nothing past the cap does not offer a way to see the rest.</summary>
    [Fact]
    public void ADimensionThatFitsInTheMenuOffersNoShowAll()
    {
        var type = Type("department", "Department", "إدارة", "Departments", "الإدارات", order: 10);

        var menu = Compose(
            [type],
            Structures((type, "org", "Org", "منظمة")),
            Records((type, "civil", "Civil", "الهندسة المدنية")));

        menu.Groups[0].HasMore.Should().BeFalse();
        menu.Groups[0].TotalValues.Should().Be(1);
    }

    /// <summary>
    /// A type on several charts has no organisation to name, so its values carry no bracket.
    /// </summary>
    /// <remarks>
    /// The same refusal to guess the list screen makes for its Parent column: "which organisation is
    /// this department in" has more than one true answer, and a bracket would pick one of them
    /// silently. ADR-0005 is what makes this a real state rather than a hypothetical — one record,
    /// several axes, by design.
    /// </remarks>
    [Fact]
    public void ATypeOnSeveralChartsNamesNoneOfThem()
    {
        var shared = Type("shared", "Department", "إدارة", "Departments", "الإدارات", order: 10);
        var other = Type("other", "Department", "إدارة", "Departments", "الإدارات", order: 20);

        var menu = Compose(
            [shared, other],
            new Dictionary<string, IReadOnlyList<StructureRef>>(StringComparer.Ordinal)
            {
                [shared.DimensionTypeId] =
                [
                    new StructureRef("one", new BilingualText("One", "واحد")),
                    new StructureRef("two", new BilingualText("Two", "اثنان")),
                ],
                [other.DimensionTypeId] = [new StructureRef("three", new BilingualText("Three", "ثلاثة"))],
            },
            Records((shared, "civil", "Civil", "الهندسة المدنية")));

        menu.Groups[0].ShowsOrganisation.Should().BeTrue("the group as a whole covers three charts");
        menu.Groups[0].EnglishOrder[0].Caption(English).Should().Be("Civil", "but this type covers two of them");
    }

    // ---- harness ---------------------------------------------------------------------------

    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en");

    private static readonly CultureInfo Arabic = CultureInfo.GetCultureInfo("ar");

    private static DimensionTypeDocument Type(
        string code, string nameEn, string nameAr, string pluralEn, string pluralAr, int order) =>
        new()
        {
            DimensionTypeId = code,
            Code = code,
            Name = new BilingualText(nameEn, nameAr),
            PluralName = new BilingualText(pluralEn, pluralAr),
            MenuOrder = order,
            ShowInMenu = true,
        };

    private static DimensionMenu Compose(params DimensionTypeDocument[] types) =>
        DimensionMenuComposition.Compose(
            types,
            new Dictionary<string, IReadOnlyList<StructureRef>>(StringComparer.Ordinal),
            new Dictionary<string, IReadOnlyList<DimensionNodeRef>>(StringComparer.Ordinal));

    private static DimensionMenu Compose(
        IReadOnlyList<DimensionTypeDocument> types,
        IReadOnlyDictionary<string, IReadOnlyList<StructureRef>> structures,
        IReadOnlyDictionary<string, IReadOnlyList<DimensionNodeRef>> records) =>
        DimensionMenuComposition.Compose(types, structures, records);

    private static Dictionary<string, IReadOnlyList<StructureRef>> Structures(
        params (DimensionTypeDocument Type, string StructureId, string NameEn, string NameAr)[] rows)
    {
        var map = new Dictionary<string, List<StructureRef>>(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            if (!map.TryGetValue(row.Type.DimensionTypeId, out var list))
            {
                map[row.Type.DimensionTypeId] = list = [];
            }

            list.Add(new StructureRef(row.StructureId, new BilingualText(row.NameEn, row.NameAr)));
        }

        return map.ToDictionary(
            pair => pair.Key, pair => (IReadOnlyList<StructureRef>)pair.Value, StringComparer.Ordinal);
    }

    private static Dictionary<string, IReadOnlyList<DimensionNodeRef>> Records(
        params (DimensionTypeDocument Type, string Code, string NameEn, string NameAr)[] rows)
    {
        var map = new Dictionary<string, List<DimensionNodeRef>>(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            if (!map.TryGetValue(row.Type.DimensionTypeId, out var list))
            {
                map[row.Type.DimensionTypeId] = list = [];
            }

            list.Add(new DimensionNodeRef(
                row.Code,
                row.Code,
                row.NameEn,
                row.NameAr,
                row.Type.DimensionTypeId,
                new EffectiveRange(new DateOnly(2024, 1, 1), null),
                IsActive: true,
                SortOrder: 0));
        }

        return map.ToDictionary(
            pair => pair.Key, pair => (IReadOnlyList<DimensionNodeRef>)pair.Value, StringComparer.Ordinal);
    }
}
