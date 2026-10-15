using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using WorkMate.Core;
using WorkMate.Dimensions.Models;
using WorkMate.Dimensions.Services;
using WorkMate.Platform.Services;
using Xunit;

namespace WorkMate.Integration.Tests;

/// <summary>
/// The Grade dimension: what the base recipe ships, how a grade is grouped, which positions use it,
/// and the two rules that keep a reference from behaving like a parent. Decision D26 C, slice F5+F7.
/// </summary>
/// <remarks>
/// <b>There is no grade step.</b> D5 and D7 described a <c>grade-step</c> dimension under a
/// <c>grade</c> one, and D26 C removes the second level outright: a position's allowed grades
/// <em>are</em> its levels. So the assertions here are about one dimension, and one of them says in
/// terms that the second type does not exist — a regression that brought it back would otherwise
/// show up only as an extra menu entry nobody looked at.
///
/// <b>A reference is not a parent, and that is what most of this file is about.</b> An allowed grade
/// never enters the closure index and never appears on a tree (D21, confirmed), so it gets none of
/// the containment map's protection for free: nothing but <c>IDimensionValidator</c> stands between
/// a position and a reference to a department. Those rules are tested here rather than left to the
/// pickers, because a picker is a control and not a rule.
/// </remarks>
[Collection(UsesTheBaseTenant.Name)]
public sealed class GradesTenantTests
{
    private readonly BaseTenantFixture _fixture;

    public GradesTenantTests(BaseTenantFixture fixture) => _fixture = fixture;

    private Task InTenantAsSystemAsync(Func<IServiceProvider, Task> work) =>
        _fixture.InTenantAsync(async services =>
        {
            using (services.GetRequiredService<ISystemOperation>().Begin("grades test"))
            {
                await work(services);
            }
        });

    // ---- what the base recipe ships -----------------------------------------------------

    /// <summary>
    /// A new tenant has a Grade dimension, named and pluralised in both languages, with the role
    /// that is how the product finds it.
    /// </summary>
    /// <remarks>
    /// <b>The role is the assertion that matters.</b> Everything else here could be derived from the
    /// code; the role could not, and is the whole reason the employee form's grade drop-down can
    /// find this dimension on a tenant that has renamed it to <em>Band</em> (FRS Rule 6) or on the
    /// demo tenant, where the type is called <c>zenith-grade</c>. See <c>DimensionTypeRole</c>.
    /// </remarks>
    [Fact]
    public async Task TheBaseRecipeShipsAGradeDimensionWithTheRoleThatIdentifiesIt() =>
        await InTenantAsSystemAsync(async services =>
        {
            var types = services.GetRequiredService<IDimensionTypeService>();
            var grade = await types.GetByCodeAsync("grade");

            grade.Should().NotBeNull("the base recipe ships the Grade dimension (D26 C)");

            grade!.StandardRole.Should().Be(
                DimensionTypeRole.Grade,
                "the role is how the product finds this dimension — never its code and never its name");

            grade.Name.En.Should().Be("Grade");
            grade.Name.Ar.Should().Be("درجة");
            grade.PluralName.En.Should().Be("Grades");
            grade.PluralName.Ar.Should().Be("الدرجات");

            grade.ShowInMenu.Should().BeTrue("it gets its own entry in the Organisation menu");
        });

    /// <summary>There is no grade-step dimension, and there is not going to be one.</summary>
    /// <remarks>
    /// D5 and D7 described one; <b>D26 C removes it</b>. A position's allowed grades are its levels,
    /// so there is nothing for a step to be a level of. Asserted by name because the alternative is
    /// that a regression reintroducing it shows up only as an extra menu entry.
    /// </remarks>
    [Fact]
    public async Task ThereIsNoGradeStepDimension() =>
        await InTenantAsSystemAsync(async services =>
        {
            var types = services.GetRequiredService<IDimensionTypeService>();

            (await types.GetByCodeAsync("grade-step")).Should().BeNull(
                "D26 C removes the step level that D5 and D7 described: a position's allowed grades "
                + "are its levels, and there is nothing below a grade");
        });

    /// <summary>
    /// The grades axis ships, carries only grades, and permits a grade inside a grade.
    /// </summary>
    /// <remarks>
    /// <b>Flat by default, groupable by permission.</b> The shipped data has no grouping at all;
    /// what is shipped is the <em>licence</em> to group — the <c>grade → grade</c> diagonal — so a
    /// tenant that wants an <em>SE</em> heading above SE-1..SE-3 can make one and a tenant that does
    /// not never meets the idea. That is the difference between this and the step concept it
    /// replaces: a step was a level the product decided they had.
    ///
    /// <b>Shipping this axis is not the base recipe guessing an organisation chart</b> (ADR-0028). A
    /// grade ladder has one level, holds no units and has nobody placed on it; the organisation
    /// structure the same recipe creates is still deliberately empty.
    /// </remarks>
    [Fact]
    public async Task TheBaseRecipeShipsAGradesAxisThatPermitsGroupingAndNothingElse() =>
        await InTenantAsSystemAsync(async services =>
        {
            var types = services.GetRequiredService<IDimensionTypeService>();
            var structures = services.GetRequiredService<IStructureService>();

            var grade = (await types.GetByCodeAsync("grade"))!;
            var axis = await structures.GetByCodeAsync("grades");

            axis.Should().NotBeNull("Organisation → Grades has to have somewhere to put a value");

            axis!.DimensionTypeIds.Should().Equal(
                [grade.DimensionTypeId], "a grade ladder has exactly one level");

            axis.PermitsRoot(grade.DimensionTypeId).Should().BeTrue("a flat ladder is all roots");

            axis.Permits(grade.DimensionTypeId, grade.DimensionTypeId).Should().BeTrue(
                "the diagonal is what lets a tenant put an SE heading above SE-1..SE-3 (D26 C)");

            axis.IsPrimaryOrganisation.Should().BeFalse("the organisation axis is the organisation axis");

            // The rule the base recipe states about itself, re-asserted from the other side: the
            // grades axis arriving does not mean the organisation chart has been guessed at.
            (await structures.GetPrimaryOrganisationAsync())!
                .DimensionTypeIds.Should().BeEmpty(
                    "every new tenant still gets an organisation chart to fill in, not one somebody guessed");
        });

    /// <summary>
    /// Grades are created flat, and one can be put under another when a tenant wants a group.
    /// </summary>
    [Fact]
    public async Task GradesAreFlatByDefaultAndOneCanBeGroupedUnderAnother() =>
        await InTenantAsSystemAsync(async services =>
        {
            var records = services.GetRequiredService<IDimensionService>();
            var graph = services.GetRequiredService<IDimensionGraphService>();
            var grade = (await services.GetRequiredService<IDimensionTypeService>().GetByCodeAsync("grade"))!;
            var axis = (await services.GetRequiredService<IStructureService>().GetByCodeAsync("grades"))!;

            var opened = new DateOnly(2024, 1, 1);

            var group = await AddAsync(records, axis.StructureId, null, grade.DimensionTypeId, "flat-se", "SE", opened);
            var first = await AddAsync(records, axis.StructureId, null, grade.DimensionTypeId, "flat-se-1", "SE-1", opened);

            // Flat: both are roots of the ladder, which is what "no grouping" looks like.
            (await graph.GetRootsAsync(axis.StructureId, opened))
                .Select(root => root.Code)
                .Should().Contain(["flat-se", "flat-se-1"]);

            // And grouping is available rather than imposed.
            var grouped = await records.MoveAsync(axis.StructureId, first, group, opened);

            grouped.Succeeded.Should().BeTrue(
                string.Join("; ", grouped.Errors.Select(error => error.Message.Value)));

            (await graph.GetChildrenAsync(axis.StructureId, group, opened))
                .Select(child => child.Code)
                .Should().Equal(["flat-se-1"], "a group is an ordinary grade value with grades under it");
        });

    // ---- used by positions --------------------------------------------------------------

    /// <summary>
    /// A grade nothing uses says so, rather than showing an empty space.
    /// </summary>
    /// <remarks>
    /// The opposite judgement from the units section on the same page, which a leaf hides entirely.
    /// A unit with nothing under it is ordinary; a grade no position allows is a grade nobody can be
    /// given, which is exactly what somebody opening it needs to be told.
    /// </remarks>
    [Fact]
    public async Task AGradeNoPositionAllowsSaysSoOnItsOwnPage()
    {
        var grade = string.Empty;

        await InTenantAsSystemAsync(async services =>
        {
            var records = services.GetRequiredService<IDimensionService>();
            var type = (await services.GetRequiredService<IDimensionTypeService>().GetByCodeAsync("grade"))!;

            grade = await ValueAsync(records, type.DimensionTypeId, "unused-g1", "Unused G1");
        });

        var page = await BaseTenantFixture.GetPageAsync(
            _fixture.Administrator, $"/Admin/Dimensions/Records/Details/{grade}");

        page.Should().Contain("Used by positions");
        page.Should().Contain("value-used-by-positions-none");
    }

    /// <summary>A grade's page names every position that allows it, with the unit each sits in.</summary>
    [Fact]
    public async Task AGradePageNamesThePositionsThatAllowIt()
    {
        var grade = string.Empty;

        await InTenantAsSystemAsync(async services =>
        {
            var records = services.GetRequiredService<IDimensionService>();
            var types = services.GetRequiredService<IDimensionTypeService>();

            var gradeType = (await types.GetByCodeAsync("grade"))!.DimensionTypeId;
            var positionType = await RoleTypeAsync(types, "usedby-position", "Usedby Position", DimensionTypeRole.Position);

            grade = await ValueAsync(records, gradeType, "usedby-se-1", "SE-1");
            var position = await ValueAsync(records, positionType, "usedby-pos-se", "Software Engineer");

            var set = await records.SetReferencesAsync(
                position, DimensionReferenceKinds.AllowedGrade, [grade]);

            set.Succeeded.Should().BeTrue(
                string.Join("; ", set.Errors.Select(error => error.Message.Value)));
        });

        var page = await BaseTenantFixture.GetPageAsync(
            _fixture.Administrator, $"/Admin/Dimensions/Records/Details/{grade}");

        page.Should().Contain("Used by positions");
        page.Should().Contain("Software Engineer");
        page.Should().Contain("usedby-pos-se", "the code is what tells two positions of one name apart");
        page.Should().NotContain("value-used-by-positions-none");
    }

    /// <summary>
    /// Retiring a grade takes it out of a position's offered list without removing the reference.
    /// </summary>
    /// <remarks>
    /// <b>The dating is in the target, never in the reference</b> — see <c>DimensionReferenceKinds</c>.
    /// That is what makes D26 G's promise cheap: removing or retiring a grade an employee holds
    /// changes what is <em>offered</em> and touches nothing that is <em>stored</em>, so asking as at
    /// a date before the retirement still answers.
    /// </remarks>
    [Fact]
    public async Task ARetiredGradeStopsBeingOfferedAndTheReferenceSurvives()
    {
        var position = string.Empty;
        var opened = new DateOnly(2024, 1, 1);
        var retired = new DateOnly(2025, 6, 30);

        await InTenantAsSystemAsync(async services =>
        {
            var records = services.GetRequiredService<IDimensionService>();
            var types = services.GetRequiredService<IDimensionTypeService>();

            var gradeType = (await types.GetByCodeAsync("grade"))!.DimensionTypeId;
            var positionType = await RoleTypeAsync(types, "retire-position", "Retire Position", DimensionTypeRole.Position);

            var staying = await ValueAsync(records, gradeType, "retire-g-stay", "Stays", opened);
            var going = await ValueAsync(records, gradeType, "retire-g-go", "Goes", opened);

            position = await ValueAsync(records, positionType, "retire-pos", "Retire Position Value", opened);

            await records.SetReferencesAsync(position, DimensionReferenceKinds.AllowedGrade, [staying, going]);

            var retire = await records.RetireAsync(going, retired);

            retire.Succeeded.Should().BeTrue(
                string.Join("; ", retire.Errors.Select(error => error.Message.Value)));
        });

        await InTenantAsSystemAsync(async services =>
        {
            var records = services.GetRequiredService<IDimensionService>();

            (await records.GetReferencesAsync(
                    position, DimensionReferenceKinds.AllowedGrade, retired.AddDays(1)))
                .Select(grade => grade.Code)
                .Should().Equal(["retire-g-stay"], "a grade that has closed is not offered");

            (await records.GetReferencesAsync(
                    position, DimensionReferenceKinds.AllowedGrade, opened))
                .Select(grade => grade.Code)
                .Should().Equal(["retire-g-stay", "retire-g-go"],
                    "the reference itself is undated, so last year still answers");
        });
    }

    // ---- the two rules that keep a reference from behaving like a parent -----------------

    /// <summary>Only a position holds allowed grades, and only a grade can be one.</summary>
    /// <remarks>
    /// A reference is declared nowhere — no structure carries it and no closure constrains it — so
    /// these two rules are the whole of what stops a position referencing a department. They live in
    /// <c>IDimensionValidator</c> so the Add/Edit form, the recipe step and F6's employee screen are
    /// refused in the same words from the same code.
    /// </remarks>
    [Fact]
    public async Task OnlyAPositionHoldsAllowedGradesAndOnlyAGradeMayBeOne() =>
        await InTenantAsSystemAsync(async services =>
        {
            var records = services.GetRequiredService<IDimensionService>();
            var types = services.GetRequiredService<IDimensionTypeService>();

            var gradeType = (await types.GetByCodeAsync("grade"))!.DimensionTypeId;
            var positionType = await RoleTypeAsync(types, "rules-position", "Rules Position", DimensionTypeRole.Position);
            var plainType = await RoleTypeAsync(types, "rules-department", "Rules Department", DimensionTypeRole.None);

            var position = await ValueAsync(records, positionType, "rules-pos", "A Position");
            var grade = await ValueAsync(records, gradeType, "rules-grade", "A Grade");
            var department = await ValueAsync(records, plainType, "rules-dept", "A Department");

            var byADepartment = await records.SetReferencesAsync(
                department, DimensionReferenceKinds.AllowedGrade, [grade]);

            byADepartment.Succeeded.Should().BeFalse("only a position has allowed grades");
            byADepartment.Errors.Should().Contain(error => error.Rule == DimensionRule.ReferenceNotPermitted);

            var toADepartment = await records.SetReferencesAsync(
                position, DimensionReferenceKinds.AllowedGrade, [department]);

            toADepartment.Succeeded.Should().BeFalse("a department is not a grade");
            toADepartment.Errors.Should().Contain(error => error.Rule == DimensionRule.ReferenceNotPermitted);

            // And the one that should work, so the two refusals above are not passing by accident.
            (await records.SetReferencesAsync(position, DimensionReferenceKinds.AllowedGrade, [grade]))
                .Succeeded.Should().BeTrue();
        });

    /// <summary>
    /// An employee's grade has to be one of their position's allowed grades. Decision D26 G.
    /// </summary>
    /// <remarks>
    /// <b>Built here in F5+F7 and called by the employee form in F6.</b> A grade control populated
    /// from the position's set is a control, not a rule: nothing in it stops a recipe, an import or
    /// a replayed form post from naming another grade. The rule is the product's, so it lives in the
    /// validator.
    /// </remarks>
    [Fact]
    public async Task AnEmployeesGradeMustBeOneOfTheirPositionsAllowedGrades() =>
        await InTenantAsSystemAsync(async services =>
        {
            var records = services.GetRequiredService<IDimensionService>();
            var types = services.GetRequiredService<IDimensionTypeService>();
            var validator = services.GetRequiredService<IDimensionValidator>();
            var today = new DateOnly(2026, 1, 1);

            var gradeType = (await types.GetByCodeAsync("grade"))!.DimensionTypeId;
            var positionType = await RoleTypeAsync(types, "empgrade-position", "Empgrade Position", DimensionTypeRole.Position);

            var engineer = await ValueAsync(records, positionType, "empgrade-se", "Software Engineer");
            var manager = await ValueAsync(records, positionType, "empgrade-hrm", "HR Manager");
            var ungraded = await ValueAsync(records, positionType, "empgrade-none", "Ungraded Post");

            var se1 = await ValueAsync(records, gradeType, "empgrade-se-1", "SE-1");
            var hrm1 = await ValueAsync(records, gradeType, "empgrade-hrm-1", "HRM-1");

            await records.SetReferencesAsync(engineer, DimensionReferenceKinds.AllowedGrade, [se1]);
            await records.SetReferencesAsync(manager, DimensionReferenceKinds.AllowedGrade, [hrm1]);

            (await validator.ValidateEmployeeGradeAsync(engineer, se1, today))
                .Should().BeEmpty("SE-1 is one of Software Engineer's levels");

            (await validator.ValidateEmployeeGradeAsync(engineer, hrm1, today))
                .Should().Contain(error => error.Rule == DimensionRule.ReferenceNotPermitted,
                    "a grade of another position is not a level of this one — D26 G's "
                    + "'changing the Position requires choosing a grade of the new Position'");

            (await validator.ValidateEmployeeGradeAsync(engineer, gradeRecordId: null, today))
                .Should().Contain(error => error.Rule == DimensionRule.ReferenceNotPermitted,
                    "a position that has levels requires one to be chosen");

            (await validator.ValidateEmployeeGradeAsync(ungraded, gradeRecordId: null, today))
                .Should().BeEmpty("a position with no grades has no level to choose, so demanding one "
                    + "would make it unusable");
        });

    // ---- helpers ------------------------------------------------------------------------

    private static async Task<string> RoleTypeAsync(
        IDimensionTypeService types,
        string code,
        string name,
        DimensionTypeRole role)
    {
        if (await types.GetByCodeAsync(code) is { } existing)
        {
            return existing.DimensionTypeId;
        }

        var created = await types.CreateAsync(
            code, new BilingualText(name, name), [], allowsSelfNesting: false, standardRole: role);

        created.Succeeded.Should().BeTrue(
            string.Join("; ", created.Errors.Select(error => error.Message.Value)));

        return created.Value!.DimensionTypeId;
    }

    /// <summary>A value of a type, unplaced — a reference is not a placement and needs no axis.</summary>
    private static async Task<string> ValueAsync(
        IDimensionService records,
        string dimensionTypeId,
        string code,
        string name,
        DateOnly? from = null)
    {
        var created = await records.CreateAsync(
            dimensionTypeId,
            code,
            new BilingualText(name, name),
            new EffectiveRange(from ?? new DateOnly(2024, 1, 1), null));

        created.Succeeded.Should().BeTrue(
            string.Join("; ", created.Errors.Select(error => error.Message.Value)));

        return created.Value!.RecordId;
    }

    /// <summary>A value created and placed on an axis in one act, the way the Add form does it.</summary>
    private static async Task<string> AddAsync(
        IDimensionService records,
        string structureId,
        string? parentRecordId,
        string dimensionTypeId,
        string code,
        string name,
        DateOnly from)
    {
        var added = await records.AddUnitAsync(
            structureId, parentRecordId, dimensionTypeId, code, new BilingualText(name, name), from);

        added.Succeeded.Should().BeTrue(
            string.Join("; ", added.Errors.Select(error => error.Message.Value)));

        return added.Value!.RecordId;
    }
}
