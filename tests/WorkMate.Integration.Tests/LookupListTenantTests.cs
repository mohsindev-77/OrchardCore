using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using WorkMate.Core;
using WorkMate.Platform.Services;
using WorkMate.Records.Models;
using WorkMate.Records.Services;
using Xunit;

namespace WorkMate.Integration.Tests;

/// <summary>
/// <see cref="ILookupListService"/>'s rules, against a real tenant.
/// </summary>
/// <remarks>
/// The safety rules are the point of this file. ADR-0014 rests on two of them — an entry cannot be
/// removed and cannot have its code changed — because an entry is the value a record stores, and a
/// record naming a code nothing resolves is a report that comes out short with nothing to say why.
/// They are enforced in the service rather than in the screen, so that the recipe step, the export
/// and any future API are covered by the same check.
/// </remarks>
[Collection(UsesTheBaseTenant.Name)]
public sealed class LookupListTenantTests
{
    private readonly BaseTenantFixture _fixture;

    public LookupListTenantTests(BaseTenantFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task AListIsCreatedWithItsEntriesAndReadBackByCode() =>
        await InTenantAsync(async lists =>
        {
            var created = await lists.CreateAsync(
                "svc-grade",
                new BilingualText("Grade", "الدرجة"),
                [
                    Item("G1", "Grade 1", "الدرجة ١", order: 10),
                    Item("G2", "Grade 2", "الدرجة ٢", order: 20),
                ]);

            created.Succeeded.Should().BeTrue(created.Describe());

            // By code and case-insensitively: a recipe that shouts a code finds the list that is
            // there rather than creating a second one beside it.
            var byCode = await lists.GetByCodeAsync("SVC-GRADE");

            byCode.Should().NotBeNull();
            byCode!.Name.Ar.Should().Be("الدرجة");
            byCode.Items.Select(item => item.Code).Should().Equal(["G1", "G2"]);
            byCode.Items.Should().AllSatisfy(item => item.IsActive.Should().BeTrue());
        });

    /// <summary>
    /// Entries come back in sort order, then English name — the rule the chart already uses for
    /// siblings, applied on the way in so that a reader, an export and a round trip all agree.
    /// </summary>
    [Fact]
    public async Task EntriesAreStoredInTheOrderAReaderIsOfferedThem() =>
        await InTenantAsync(async lists =>
        {
            var created = await lists.CreateAsync(
                "svc-order",
                new BilingualText("Order", "ترتيب"),
                [
                    Item("C", "Charlie", order: 30),
                    Item("A", "Alpha", order: 10),
                    Item("B2", "Bravo two", order: 20),
                    Item("B1", "Bravo one", order: 20),
                ]);

            created.Succeeded.Should().BeTrue(created.Describe());

            created.Value!.Items.Select(item => item.Code)
                .Should().Equal(["A", "B1", "B2", "C"], "sort order first, then the English name");
        });

    /// <summary>
    /// The rule ADR-0014 exists for: an entry the tenant holds cannot be dropped out of the set.
    /// </summary>
    [Fact]
    public async Task AnEntryThatIsAlreadyStoredCannotBeRemoved() =>
        await InTenantAsync(async lists =>
        {
            var created = await lists.CreateAsync(
                "svc-remove",
                new BilingualText("Removable", "قابلة"),
                [Item("KEEP", "Keep", order: 10), Item("GONE", "Gone", order: 20)]);

            created.Succeeded.Should().BeTrue(created.Describe());

            var withoutOne = await lists.UpdateAsync(
                created.Value!.LookupListId,
                new BilingualText("Removable", "قابلة"),
                [Item("KEEP", "Keep", order: 10)]);

            withoutOne.Succeeded.Should().BeFalse();
            withoutOne.Errors.Should().ContainSingle()
                .Which.Rule.Should().Be(RecordRule.ImmutableOnceInUse);

            // Named, so the reader knows which one, and told what to do instead.
            withoutOne.Describe().Should().Contain("GONE").And.Contain("Retire");

            var unchanged = await lists.GetAsync(created.Value.LookupListId);

            unchanged!.Items.Select(item => item.Code).Should().Equal(["KEEP", "GONE"],
                "a refused update writes nothing at all");
        });

    /// <summary>
    /// Changing an entry's code is the same destruction by another route, and is refused the same
    /// way — it arrives as the old code being dropped and a new one appearing.
    /// </summary>
    [Fact]
    public async Task AnEntrysCodeCannotBeChanged() =>
        await InTenantAsync(async lists =>
        {
            var created = await lists.CreateAsync(
                "svc-recode",
                new BilingualText("Recode", "إعادة"),
                [Item("OLD", "Old name")]);

            created.Succeeded.Should().BeTrue(created.Describe());

            var renamed = await lists.UpdateAsync(
                created.Value!.LookupListId,
                new BilingualText("Recode", "إعادة"),
                [Item("NEW", "Old name")]);

            renamed.Succeeded.Should().BeFalse();
            renamed.Errors.Should().Contain(error => error.Rule == RecordRule.ImmutableOnceInUse);
            renamed.Describe().Should().Contain("OLD");
        });

    /// <summary>
    /// Retiring is the supported way to stop offering an entry, and it keeps everything.
    /// </summary>
    [Fact]
    public async Task AnEntryIsRetiredRatherThanRemoved() =>
        await InTenantAsync(async lists =>
        {
            var created = await lists.CreateAsync(
                "svc-retire",
                new BilingualText("Retire", "تقاعد"),
                [Item("LIVE", "Live"), Item("OLD", "Old")]);

            created.Succeeded.Should().BeTrue(created.Describe());

            var retired = await lists.UpdateAsync(
                created.Value!.LookupListId,
                new BilingualText("Retire", "تقاعد"),
                [Item("LIVE", "Live"), Item("OLD", "Old", active: false)]);

            retired.Succeeded.Should().BeTrue(retired.Describe());

            var after = await lists.GetByCodeAsync("svc-retire");

            after!.Items.Should().HaveCount(2, "nothing is lost by retiring");
            after.Items.Single(item => item.Code == "OLD").IsActive.Should().BeFalse();
            after.Items.Single(item => item.Code == "LIVE").IsActive.Should().BeTrue();
        });

    /// <summary>Renaming an entry is always allowed: the name is presentation, the code is the data.</summary>
    [Fact]
    public async Task AnEntrysNameMayAlwaysChange() =>
        await InTenantAsync(async lists =>
        {
            var created = await lists.CreateAsync(
                "svc-rename",
                new BilingualText("Rename", "تسمية"),
                [Item("G1", "Grade 1", "الدرجة ١")]);

            created.Succeeded.Should().BeTrue(created.Describe());

            var renamed = await lists.UpdateAsync(
                created.Value!.LookupListId,
                new BilingualText("Pay grade", "درجة الراتب"),
                [Item("G1", "Pay grade 1", "درجة الراتب ١")]);

            renamed.Succeeded.Should().BeTrue(renamed.Describe());

            var after = await lists.GetByCodeAsync("svc-rename");

            after!.Name.En.Should().Be("Pay grade");
            after.Items.Single().Name.Ar.Should().Be("درجة الراتب ١");
            after.Items.Single().Code.Should().Be("G1", "the code is what records hold");
        });

    [Fact]
    public async Task TwoListsCannotShareACodeHoweverItIsSpelt() =>
        await InTenantAsync(async lists =>
        {
            var first = await lists.CreateAsync("svc-dup", new BilingualText("First", "الأولى"), []);

            first.Succeeded.Should().BeTrue(first.Describe());

            var second = await lists.CreateAsync("SVC-DUP", new BilingualText("Second", "الثانية"), []);

            second.Succeeded.Should().BeFalse();
            second.Errors.Should().ContainSingle().Which.Rule.Should().Be(RecordRule.CodeUniqueness);
        });

    [Fact]
    public async Task TwoEntriesInOneListCannotShareACode() =>
        await InTenantAsync(async lists =>
        {
            var created = await lists.CreateAsync(
                "svc-dupitem",
                new BilingualText("Duplicates", "مكررة"),
                [Item("G1", "One"), Item("g1", "Also one")]);

            created.Succeeded.Should().BeFalse();
            created.Errors.Should().Contain(error => error.Rule == RecordRule.CodeUniqueness);
        });

    [Fact]
    public async Task AMalformedCodeIsRefusedAndSaysWhichKind() =>
        await InTenantAsync(async lists =>
        {
            var badList = await lists.CreateAsync("1bad", new BilingualText("Bad", "سيئة"), []);

            badList.Succeeded.Should().BeFalse();
            badList.Errors.Should().ContainSingle().Which.Rule.Should().Be(RecordRule.CodeFormat);

            var badItem = await lists.CreateAsync(
                "svc-baditem",
                new BilingualText("Bad entry", "مدخل"),
                [Item("has space", "Spaced")]);

            badItem.Succeeded.Should().BeFalse();
            badItem.Errors.Should().Contain(error => error.Rule == RecordRule.CodeFormat);
        });

    /// <summary>
    /// English is required, here as everywhere. Arabic is not, unless the tenant has turned
    /// <c>RequireArabicNames</c> on — ADR-0003's addendum, asked through the same one-question seam.
    /// </summary>
    [Fact]
    public async Task EnglishIsRequiredAndArabicIsNot() =>
        await InTenantAsync(async lists =>
        {
            var noEnglish = await lists.CreateAsync(
                "svc-noen",
                new BilingualText(string.Empty, "بلا إنجليزية"),
                []);

            noEnglish.Succeeded.Should().BeFalse();
            noEnglish.Errors.Should().ContainSingle().Which.Rule.Should().Be(RecordRule.NameRequired);

            var noArabic = await lists.CreateAsync(
                "svc-noar",
                new BilingualText("No Arabic", string.Empty),
                [Item("X", "Entry with no Arabic")]);

            noArabic.Succeeded.Should().BeTrue(noArabic.Describe());

            // And the entry, not only the list: a required Arabic half on an entry would have been
            // the same rule enforced in a second place with a different answer.
            var entryWithoutEnglish = await lists.CreateAsync(
                "svc-noenitem",
                new BilingualText("Entries", "مدخلات"),
                [new LookupListItem("X", new BilingualText(string.Empty, "عربي"))]);

            entryWithoutEnglish.Succeeded.Should().BeFalse();
            entryWithoutEnglish.Errors.Should().Contain(error => error.Rule == RecordRule.NameRequired);
        });

    /// <summary>
    /// A list may be deleted only while it is empty, which is the one state in which nothing can be
    /// relying on it.
    /// </summary>
    /// <remarks>
    /// Creating and deleting in <em>separate</em> shell scopes, unlike the tests above, because
    /// that is the only way it ever happens: a list is created by a recipe or a screen and deleted
    /// by a later request. YesSql cannot delete a document whose own save has not yet flushed —
    /// "the object to update was not found in identity map" — and writing the test as one scope
    /// would be pinning a sequence no caller performs.
    /// </remarks>
    [Fact]
    public async Task AnEmptyListIsDeletedAndAPopulatedOneIsNot()
    {
        string emptyId = string.Empty;
        string populatedId = string.Empty;

        await InTenantAsync(async lists =>
        {
            var empty = await lists.CreateAsync("svc-empty", new BilingualText("Empty", "فارغة"), []);

            empty.Succeeded.Should().BeTrue(empty.Describe());
            emptyId = empty.Value!.LookupListId;

            var populated = await lists.CreateAsync(
                "svc-populated",
                new BilingualText("Populated", "ممتلئة"),
                [Item("X", "Something")]);

            populated.Succeeded.Should().BeTrue(populated.Describe());
            populatedId = populated.Value!.LookupListId;
        });

        await InTenantAsync(async lists =>
        {
            var deleted = await lists.DeleteAsync(emptyId);

            deleted.Succeeded.Should().BeTrue(deleted.Describe());

            var refused = await lists.DeleteAsync(populatedId);

            refused.Succeeded.Should().BeFalse();
            refused.Errors.Should().ContainSingle().Which.Rule.Should().Be(RecordRule.LookupListNotEmpty);
        });

        await InTenantAsync(async lists =>
        {
            (await lists.GetByCodeAsync("svc-empty")).Should().BeNull();
            (await lists.GetByCodeAsync("svc-populated")).Should().NotBeNull();
        });
    }

    [Fact]
    public async Task ASystemDefinedListIsNotDeletedEvenWhenEmpty()
    {
        string shippedId = string.Empty;

        await InTenantAsync(async lists =>
        {
            var shipped = await lists.CreateAsync(
                "svc-shipped",
                new BilingualText("Shipped", "مُرفقة"),
                [],
                isSystemDefined: true);

            shipped.Succeeded.Should().BeTrue(shipped.Describe());
            shippedId = shipped.Value!.LookupListId;
        });

        await InTenantAsync(async lists =>
        {
            var refused = await lists.DeleteAsync(shippedId);

            refused.Succeeded.Should().BeFalse();
            refused.Errors.Should().ContainSingle().Which.Rule.Should().Be(RecordRule.LookupListNotEmpty);
        });
    }

    [Fact]
    public async Task AnUnknownIdIsReportedRatherThanIgnored() =>
        await InTenantAsync(async lists =>
        {
            var result = await lists.UpdateAsync("nosuchid", new BilingualText("X", "س"), []);

            result.Succeeded.Should().BeFalse();
            result.Errors.Should().ContainSingle().Which.Rule.Should().Be(RecordRule.UnknownLookupList);
        });

    /// <summary>
    /// The permission is checked in the service, not only in the controller, so a recipe step or an
    /// API calling it without authority is refused too.
    /// </summary>
    /// <remarks>
    /// Run <em>outside</em> the system-operation scope every other test here enters, because that
    /// scope is exactly the thing that grants authority to a recipe step. There is no HTTP context
    /// and no system scope here, which is the background-job case
    /// <c>RecordsAuthorisation.AuthoriseAsync</c> deliberately refuses rather than exempts.
    /// </remarks>
    [Fact]
    public async Task AWriteWithNoAuthorityIsRefused() =>
        await _fixture.InTenantAsync(async services =>
        {
            var lists = services.GetRequiredService<ILookupListService>();

            var result = await lists.CreateAsync("svc-noauth", new BilingualText("No", "لا"), []);

            result.IsAuthorised.Should().BeFalse();
            result.Errors.Should().BeEmpty(
                "telling somebody which rules their unauthorised request would also have broken "
                + "leaks the configuration it was refused");

            (await lists.GetByCodeAsync("svc-noauth")).Should().BeNull();
        });

    // ---- harness ---------------------------------------------------------------------------

    private static LookupListItem Item(
        string code,
        string nameEn,
        string nameAr = "",
        bool active = true,
        int order = 0) =>
        new(code, new BilingualText(nameEn, nameAr), active, order);

    private Task InTenantAsync(Func<ILookupListService, Task> work) =>
        _fixture.InTenantAsync(async services =>
        {
            using (services.GetRequiredService<ISystemOperation>().Begin("lookup list test"))
            {
                await work(services.GetRequiredService<ILookupListService>());
            }
        });
}
