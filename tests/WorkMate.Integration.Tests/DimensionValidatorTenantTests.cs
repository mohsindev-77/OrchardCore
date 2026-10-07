using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using WorkMate.Core;
using WorkMate.Dimensions.Services;
using Xunit;

namespace WorkMate.Integration.Tests;

/// <summary>
/// The rules of architecture section 6, now that they live in one service.
/// </summary>
/// <remarks>
/// Four of these could not be enforced at all before this session: permitted level, level
/// skipping, self-nesting, and the advisory warning about a parent that is not yet effective.
/// Record code uniqueness is the fifth, and it is the one the module carried as a known gap.
/// </remarks>
[Collection(UsesTheBaseTenant.Name)]
public sealed class DimensionValidatorTenantTests
{
    private readonly BaseTenantFixture _tenant;

    public DimensionValidatorTenantTests(BaseTenantFixture tenant) => _tenant = tenant;

    private static readonly DateOnly Opened = new(2024, 1, 1);

    /// <summary>A strict three-level axis, which is what the level rules are about.</summary>
    private static async Task<(string Structure, DimensionGraphScenario.Types Types)> StrictAxisAsync(
        IServiceProvider services,
        string code,
        bool allowSkipLevel)
    {
        var types = await DimensionGraphScenario.TypesAsync(services);

        var created = await services.GetRequiredService<IStructureService>().CreateAsync(
            code,
            new BilingualText(code, code + "-ar"),
            [types.Division, types.Department, types.Section],
            allowSkipLevel,
            isStrict: true,
            isPrimaryOrganisation: false);

        created.Succeeded.Should().BeTrue(
            string.Join("; ", created.Errors.Select(error => error.Message.Value)));

        return (created.Value!.StructureId, types);
    }

    // ---- record code uniqueness: the gap this session closes --------------------------

    [Fact]
    public async Task ARecordCannotReuseACodeAnotherRecordHolds() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var types = await DimensionGraphScenario.TypesAsync(services);
            var records = services.GetRequiredService<IDimensionService>();

            var first = await records.CreateAsync(
                types.Division, "VAL-UNIQUE", new BilingualText("First", "الأول"),
                new EffectiveRange(Opened, null));

            first.Succeeded.Should().BeTrue(
                string.Join("; ", first.Errors.Select(error => error.Message.Value)));

            // A different dimension type: a code is unique within the tenant, not within a type.
            var duplicate = await records.CreateAsync(
                types.Department, "VAL-UNIQUE", new BilingualText("Second", "الثاني"),
                new EffectiveRange(Opened, null));

            duplicate.Succeeded.Should().BeFalse();
            duplicate.Errors.Should().Contain(error => error.Rule == DimensionRule.CodeUniqueness);
        });

    [Fact]
    public async Task TwoRowsOfOneImportCannotShareACode() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            // The case that defeated the handler-based attempt and the reason the batch exists.
            // Neither row is committed when the second is validated, so nothing in the database
            // can see the clash; only the batch can.
            var types = await DimensionGraphScenario.TypesAsync(services);
            var records = services.GetRequiredService<IDimensionService>();
            var batch = services.GetRequiredService<IDimensionValidator>().BeginBatch();

            var first = await records.CreateAsync(
                types.Division, "VAL-BATCH", new BilingualText("First", "الأول"),
                new EffectiveRange(Opened, null), batch);

            first.Succeeded.Should().BeTrue(
                string.Join("; ", first.Errors.Select(error => error.Message.Value)));

            var second = await records.CreateAsync(
                types.Division, "VAL-BATCH", new BilingualText("Second", "الثاني"),
                new EffectiveRange(Opened, null), batch);

            second.Succeeded.Should().BeFalse(
                "an import creating five hundred rows must catch duplicates between its own rows");

            second.Errors.Should().Contain(error => error.Rule == DimensionRule.CodeUniqueness);
        });

    [Fact]
    public async Task ARetiredRecordStillHoldsItsCode() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            // Architecture section 6: uniqueness holds "including against retired records",
            // because reusing a retired unit's code makes every historical report ambiguous
            // about which one it means.
            var (structure, types) = await StrictAxisAsync(services, "val-heldcode", allowSkipLevel: false);
            var records = services.GetRequiredService<IDimensionService>();

            var original = await records.CreateAsync(
                types.Division, "VAL-RETIRED", new BilingualText("Original", "الأصل"),
                new EffectiveRange(Opened, null));

            original.Succeeded.Should().BeTrue();

            (await records.RetireAsync(original.Value!.RecordId, new DateOnly(2025, 1, 1)))
                .Succeeded.Should().BeTrue();

            var reuse = await records.CreateAsync(
                types.Division, "VAL-RETIRED", new BilingualText("Reused", "معاد"),
                new EffectiveRange(new DateOnly(2025, 1, 1), null));

            reuse.Succeeded.Should().BeFalse();
            reuse.Errors.Should().Contain(error => error.Rule == DimensionRule.CodeUniqueness);
        });

    [Fact]
    public async Task EditingARecordDoesNotReportItAsADuplicateOfItself() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var types = await DimensionGraphScenario.TypesAsync(services);
            var validator = services.GetRequiredService<IDimensionValidator>();
            var records = services.GetRequiredService<IDimensionService>();

            var created = await records.CreateAsync(
                types.Division, "VAL-SELF", new BilingualText("Self", "ذات"),
                new EffectiveRange(Opened, null));

            created.Succeeded.Should().BeTrue();

            var errors = await validator.ValidateRecordAsync(
                created.Value!.RecordId,
                types.Division,
                "VAL-SELF",
                new BilingualText("Renamed", "معاد التسمية"),
                new EffectiveRange(Opened, null));

            errors.Should().NotContain(error => error.Rule == DimensionRule.CodeUniqueness);
        });

    // ---- level rules: new this session ------------------------------------------------

    [Fact]
    public async Task AParentBelowTheChildInTheLevelOrderIsRefused() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var (structure, types) = await StrictAxisAsync(services, "val-inverted", allowSkipLevel: true);
            var graph = services.GetRequiredService<IDimensionGraphService>();

            var division = await DimensionGraphScenario.RecordAsync(services, types.Division, "vi-div", Opened);
            var section = await DimensionGraphScenario.RecordAsync(services, types.Section, "vi-sec", Opened);

            // A division under a section inverts the hierarchy.
            var refused = await graph.MoveAsync(structure, division, section, Opened);

            refused.Succeeded.Should().BeFalse();
            refused.Errors.Should().Contain(error => error.Rule == DimensionRule.ParentTypeNotPermitted);
        });

    /// <summary>
    /// An axis built from a plain chain still refuses what skipping a level used to mean.
    /// </summary>
    /// <remarks>
    /// The rule reported is <see cref="DimensionRule.ParentTypeNotPermitted"/> rather than
    /// <c>LevelSkipping</c> since ADR-0010: a skipped level is no longer a category of its own,
    /// it is simply a pairing the structure does not declare. The behaviour a customer sees —
    /// Section under Division is refused on an axis that was not asked to allow it — is unchanged,
    /// which is the whole point of deriving the map rather than inventing one.
    /// </remarks>
    [Fact]
    public async Task APairingTheStructureDoesNotDeclareIsRefused() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var (structure, types) = await StrictAxisAsync(services, "val-noskip", allowSkipLevel: false);
            var graph = services.GetRequiredService<IDimensionGraphService>();

            var division = await DimensionGraphScenario.RecordAsync(services, types.Division, "vn-div", Opened);
            var section = await DimensionGraphScenario.RecordAsync(services, types.Section, "vn-sec", Opened);

            // Section sits two levels below division, so the derived map has no Division > Section.
            var refused = await graph.MoveAsync(structure, section, division, Opened);

            refused.Succeeded.Should().BeFalse();
            refused.Errors.Should().Contain(error => error.Rule == DimensionRule.ParentTypeNotPermitted);
        });

    [Fact]
    public async Task SkippingALevelIsAllowedWhenTheStructurePermitsIt() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            // The same placement on a structure that allows skipping. Architecture section 6 is
            // explicit that this is a structure setting, never a per-node exception: an
            // exception granted per node is one nobody can audit.
            var (structure, types) = await StrictAxisAsync(services, "val-skip", allowSkipLevel: true);
            var graph = services.GetRequiredService<IDimensionGraphService>();

            var division = await DimensionGraphScenario.RecordAsync(services, types.Division, "vskip-div", Opened);
            var section = await DimensionGraphScenario.RecordAsync(services, types.Section, "vskip-sec", Opened);

            (await graph.MoveAsync(structure, section, division, Opened)).Succeeded.Should().BeTrue();
        });

    [Fact]
    public async Task SelfNestingIsRefusedUnlessTheTypeDeclaresIt() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var (structure, types) = await StrictAxisAsync(services, "val-selfnest", allowSkipLevel: true);
            var graph = services.GetRequiredService<IDimensionGraphService>();

            // The scenario's department does not declare self-nesting; its section does.
            var outer = await DimensionGraphScenario.RecordAsync(services, types.Department, "vsn-outer", Opened);
            var inner = await DimensionGraphScenario.RecordAsync(services, types.Department, "vsn-inner", Opened);

            var refused = await graph.MoveAsync(structure, inner, outer, Opened);

            refused.Succeeded.Should().BeFalse();
            refused.Errors.Should().Contain(error => error.Rule == DimensionRule.SelfNesting);

            var outerSection = await DimensionGraphScenario.RecordAsync(services, types.Section, "vsn-so", Opened);
            var innerSection = await DimensionGraphScenario.RecordAsync(services, types.Section, "vsn-si", Opened);

            (await graph.MoveAsync(structure, innerSection, outerSection, Opened)).Succeeded.Should().BeTrue(
                "a section declares self-nesting, so a section inside a section is allowed");
        });

    [Fact]
    public async Task AParentNotYetEffectiveWarnsButDoesNotBlock() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            // The one advisory rule. Customers legitimately pre-build next year's structure,
            // and refusing that would make the engine unusable for a case it was designed for.
            var (structure, types) = await StrictAxisAsync(services, "val-future", allowSkipLevel: true);
            var graph = services.GetRequiredService<IDimensionGraphService>();

            var futureParent = await DimensionGraphScenario.RecordAsync(
                services, types.Division, "vf-parent", new DateOnly(2030, 1, 1));

            var child = await DimensionGraphScenario.RecordAsync(services, types.Department, "vf-child", Opened);

            var moved = await graph.MoveAsync(structure, child, futureParent, Opened);

            moved.Succeeded.Should().BeTrue("the placement is allowed");

            moved.Errors.Should().Contain(
                error => error.Rule == DimensionRule.ParentNotEffectiveWhenChildIs,
                "but the caller is warned, because this is usually a dating mistake");

            moved.Errors.Should().AllSatisfy(error => error.IsAdvisory.Should().BeTrue());
        });

    [Fact]
    public async Task ARetiredParentRefusesANewPlacementRatherThanWarn() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            // Unlike a parent that has not started yet, a parent that has already closed is not
            // a dating mistake a customer might mean: placing a child under it from here on would
            // leave a live unit under a closed one with no end date.
            var (structure, types) = await StrictAxisAsync(services, "val-retired", allowSkipLevel: true);
            var graph = services.GetRequiredService<IDimensionGraphService>();
            var records = services.GetRequiredService<IDimensionService>();

            var parent = await DimensionGraphScenario.RecordAsync(services, types.Division, "vr-parent", Opened);
            var child = await DimensionGraphScenario.RecordAsync(services, types.Department, "vr-child", Opened);

            (await records.RetireAsync(parent, new DateOnly(2025, 1, 1))).Succeeded.Should().BeTrue();

            var refused = await graph.MoveAsync(structure, child, parent, new DateOnly(2025, 6, 1));

            refused.Succeeded.Should().BeFalse();
            refused.Errors.Should().ContainSingle().Which.Rule.Should().Be(DimensionRule.ParentRetired);
        });

    // ---- deletion ---------------------------------------------------------------------

    [Fact]
    public async Task ARecordNothingReferencesIsCleanToDelete() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var types = await DimensionGraphScenario.TypesAsync(services);
            var validator = services.GetRequiredService<IDimensionValidator>();

            var orphan = await DimensionGraphScenario.RecordAsync(services, types.Division, "vd-clean", Opened);

            var assessment = await validator.AssessDeletionAsync(orphan);

            assessment.Outcome.Should().Be(DeletionOutcome.Clean);
            assessment.Blockers.Should().BeEmpty();
        });

    [Fact]
    public async Task ARecordWithLiveEmployeesIsBlockedAndSaysWhy() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var types = await DimensionGraphScenario.TypesAsync(services);
            var structure = await DimensionGraphScenario.StructureAsync(services, "val-delete-live");
            var validator = services.GetRequiredService<IDimensionValidator>();
            var assignments = services.GetRequiredService<IEmployeeAssignmentService>();

            var department = await DimensionGraphScenario.RecordAsync(services, types.Department, "vd-live", Opened);

            (await assignments.PlaceAsync("vd-employee", structure, department, Opened))
                .Succeeded.Should().BeTrue();

            var assessment = await validator.AssessDeletionAsync(department);

            assessment.Outcome.Should().Be(DeletionOutcome.Blocked);

            assessment.Blockers.Should().NotBeEmpty(
                "architecture section 6: a blocked deletion lists its blockers so the user can "
                + "act, and is never a generic failure message");

            assessment.Blockers.Should().AllSatisfy(blocker =>
                blocker.Rule.Should().Be(DimensionRule.DeletionBlocked));
        });

    [Fact]
    public async Task ARecordReferencedOnlyByHistoryIsRetiredRatherThanDeleted() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var types = await DimensionGraphScenario.TypesAsync(services);
            var structure = await DimensionGraphScenario.StructureAsync(services, "val-delete-past");
            var validator = services.GetRequiredService<IDimensionValidator>();
            var assignments = services.GetRequiredService<IEmployeeAssignmentService>();

            var department = await DimensionGraphScenario.RecordAsync(services, types.Department, "vd-past", Opened);

            (await assignments.PlaceAsync("vd-leaver", structure, department, Opened))
                .Succeeded.Should().BeTrue();

            (await assignments.EndAsync("vd-leaver", structure, new DateOnly(2025, 1, 1)))
                .Succeeded.Should().BeTrue();

            var assessment = await validator.AssessDeletionAsync(department);

            assessment.Outcome.Should().Be(
                DeletionOutcome.RetireOnly,
                "the record is still needed to resolve a past period, but nothing live points at it");
        });

    [Fact]
    public async Task ARecordWithChildrenIsBlocked() =>
        await _tenant.InTenantAsSystemAsync(async services =>
        {
            var (structure, types) = await StrictAxisAsync(services, "val-delete-parent", allowSkipLevel: true);
            var validator = services.GetRequiredService<IDimensionValidator>();
            var graph = services.GetRequiredService<IDimensionGraphService>();

            var parent = await DimensionGraphScenario.RecordAsync(services, types.Division, "vd-parent", Opened);
            var child = await DimensionGraphScenario.RecordAsync(services, types.Department, "vd-child", Opened);

            (await graph.MoveAsync(structure, child, parent, Opened)).Succeeded.Should().BeTrue();

            var assessment = await validator.AssessDeletionAsync(parent);

            assessment.Outcome.Should().Be(
                DeletionOutcome.Blocked,
                "deleting a node with a subtree under it would orphan the subtree");
        });
}
