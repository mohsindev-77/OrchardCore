using FluentAssertions;
using WorkMate.Core;
using WorkMate.Records.Models;
using WorkMate.Records.Services;
using Xunit;

namespace WorkMate.Records.Tests;

/// <summary>
/// What a bound field offers, and — the half that matters — what it refuses to throw away.
/// </summary>
/// <remarks>
/// A unit test with a fake lookup, because every case here is a statement about one list and one
/// stored value, and standing a tenant up to assert them would hide the table behind a fixture.
/// The tenant tests prove the same rules reach a real screen.
///
/// <b>The free-text fallback is what makes binding safe to ship to an existing tenant.</b> The
/// migration binds the standard fields on every tenant, including ones created long before the
/// lists existed. If a missing list rendered an empty dropdown, those tenants would lose the
/// ability to type a value the day they upgraded — a regression dressed as a feature. So the
/// fallback is tested first and in several shapes.
/// </remarks>
public sealed class LookupChoiceTests
{
    [Fact]
    public async Task AFieldBoundToNothingFallsBackToFreeText()
    {
        var choices = await Provider().ForAsync(listCode: null, storedValue: "anything");

        choices.IsBound.Should().BeFalse();
        choices.Options.Should().BeEmpty();
    }

    [Fact]
    public async Task AFieldBoundToAListTheTenantDoesNotHaveFallsBackToFreeText()
    {
        var choices = await Provider().ForAsync("grade", "G1");

        choices.IsBound.Should().BeFalse(
            "a tenant that has not applied the standard lists must keep the box it has always had");
    }

    [Fact]
    public async Task AFieldBoundToAnEmptyListFallsBackToFreeText()
    {
        var choices = await Provider(List("grade")).ForAsync("grade", "G1");

        choices.IsBound.Should().BeFalse();
    }

    /// <summary>
    /// A list whose every entry is retired offers nothing, unless this record holds one of them.
    /// </summary>
    [Fact]
    public async Task AListOfOnlyRetiredEntriesOffersNothingUnlessTheRecordHoldsOne()
    {
        var list = List("grade", Item("G1", "Grade 1", active: false));

        (await Provider(list).ForAsync("grade", storedValue: "")).IsBound.Should().BeFalse();

        var holding = await Provider(list).ForAsync("grade", "G1");

        holding.IsBound.Should().BeTrue("the value on this record has to stay selectable");
        holding.Options.Should().ContainSingle().Which.IsRetired.Should().BeTrue();
    }

    /// <summary>
    /// A bound field offers the active entries, in the order the list holds them.
    /// </summary>
    /// <remarks>
    /// <b>Offered in stored order, not re-sorted here.</b> <c>LookupListService</c> orders the
    /// entries on every write — by sort order, then English name — so the document a reader is
    /// offered is already in the order somebody chose. Sorting again in the control would be the
    /// same rule implemented twice, and two implementations of one ordering rule disagree on
    /// exactly the case it exists for: the tie. The list below is therefore built the way the
    /// service would have stored it.
    /// </remarks>
    [Fact]
    public async Task ABoundFieldOffersTheActiveEntriesInTheOrderTheListHoldsThem()
    {
        var list = List(
            "grade",
            Item("G1", "Grade 1", order: 10),
            Item("G2", "Grade 2", order: 20),
            Item("G9", "Grade 9", order: 90, active: false));

        var choices = await Provider(list).ForAsync("grade", storedValue: "");

        choices.IsBound.Should().BeTrue();
        choices.StoredValueIsUnmatched.Should().BeFalse();
        choices.Options.Select(option => option.Value).Should().Equal(["G1", "G2"],
            "a retired entry nobody holds is not offered");
    }

    /// <summary>
    /// The case the brief names: a record holding free text that matches no entry keeps its value
    /// and is shown as unmatched.
    /// </summary>
    /// <remarks>
    /// This is the whole of "never rewrite an existing value" at the control level. Without the
    /// extra option the <c>&lt;select&gt;</c> could not represent what the record holds, so the
    /// first save after binding would quietly replace it with whatever happened to be first.
    /// </remarks>
    [Fact]
    public async Task AValueInNoEntryIsOfferedAsItselfAndMarkedUnmatched()
    {
        var list = List("grade", Item("G1", "Grade 1", order: 10));

        var choices = await Provider(list).ForAsync("grade", "Grade One");

        choices.IsBound.Should().BeTrue();
        choices.StoredValueIsUnmatched.Should().BeTrue();

        var unmatched = choices.Options.Should().ContainSingle(option => option.IsUnmatched).Subject;

        unmatched.Value.Should().Be("Grade One", "the value is kept exactly as it is stored");
        unmatched.Label.Should().Be("Grade One");

        choices.Options.Select(option => option.Value).Should().Contain("G1");
    }

    /// <summary>
    /// A retired entry a record holds is offered and marked, rather than treated as unmatched.
    /// </summary>
    /// <remarks>
    /// The two are different and a reader needs to be able to tell them apart: a retired entry is a
    /// value the list knows and no longer offers, and an unmatched one is a value the list has
    /// never heard of. Reporting the first as the second would send somebody to the reconciliation
    /// screen to fix something that is not broken.
    /// </remarks>
    [Fact]
    public async Task ARetiredEntryTheRecordHoldsIsOfferedAndIsNotUnmatched()
    {
        var list = List(
            "grade",
            Item("G1", "Grade 1", order: 10),
            Item("G9", "Grade 9 (old)", order: 90, active: false));

        var choices = await Provider(list).ForAsync("grade", "G9");

        choices.StoredValueIsUnmatched.Should().BeFalse();

        var retired = choices.Options.Should().ContainSingle(option => option.IsRetired).Subject;

        retired.Value.Should().Be("G9");
        choices.Options[^1].Should().Be(retired, "it is offered last; it is not a choice to make again");
    }

    [Fact]
    public async Task AStoredValueIsMatchedWhateverItsCase()
    {
        var list = List("grade", Item("G1", "Grade 1", order: 10));

        var choices = await Provider(list).ForAsync("grade", "g1");

        choices.StoredValueIsUnmatched.Should().BeFalse(
            "a code shouted by an import names the same entry");
    }

    /// <summary>
    /// The label carries the name and the code, and falls back when there is no Arabic.
    /// </summary>
    [Fact]
    public async Task TheLabelCarriesTheNameAndTheCode()
    {
        var list = List("grade", Item("G1", "Grade 1", order: 10));

        var choices = await Provider(list).ForAsync("grade", "G1");

        choices.Options.Single(option => option.Value == "G1").Label
            .Should().Be("Grade 1 (G1)", "the code is what the record stores and what a payroll file contains");
    }

    [Fact]
    public async Task AnEntryWithNoNameAtAllFallsBackToItsCode()
    {
        var list = List("grade", new LookupListItem("G1", new BilingualText(string.Empty, string.Empty), SortOrder: 10));

        var choices = await Provider(list).ForAsync("grade", "G1");

        choices.Options.Single().Label.Should().Be("G1", "a reader is never shown a blank option");
    }

    // ---- harness ---------------------------------------------------------------------------

    private static LookupChoiceProvider Provider(params LookupListDocument[] lists) =>
        new(new FakeLookupListLookup(lists));

    private static LookupListDocument List(string code, params LookupListItem[] items) =>
        new()
        {
            LookupListId = code,
            Code = code,
            Name = new BilingualText(code, string.Empty),
            Items = items,
        };

    private static LookupListItem Item(string code, string nameEn, bool active = true, int order = 0) =>
        new(code, new BilingualText(nameEn, string.Empty), active, order);

    private sealed class FakeLookupListLookup : ILookupListLookup
    {
        private readonly IReadOnlyList<LookupListDocument> _lists;

        public FakeLookupListLookup(IReadOnlyList<LookupListDocument> lists) => _lists = lists;

        public Task<LookupListDocument?> GetByCodeAsync(string code, CancellationToken cancellationToken = default) =>
            Task.FromResult(_lists.FirstOrDefault(list => LookupCodes.Same(list.Code, code)));

        public Task<IReadOnlyList<LookupListDocument>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_lists);
    }
}
