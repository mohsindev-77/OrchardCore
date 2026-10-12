using Microsoft.Extensions.Localization;
using OrchardCore.ContentManagement;
using OrchardCore.ContentManagement.Metadata;
using OrchardCore.ContentManagement.Metadata.Models;
using OrchardCore.ContentFields.Fields;
using OrchardCore.Flows.Models;
using OrchardCore.AuditTrail.Services;
using OrchardCore.AuditTrail.Services.Models;
using WorkMate.Records.Indexes;
using WorkMate.Records.Models;
using YesSql;

namespace WorkMate.Records.Services;

/// <summary>Where one bound value is stored, as the scan walks it.</summary>
/// <param name="SectionName">
/// The bag on the employee that holds the rows, or empty for a value on the employee core.
/// </param>
/// <param name="ItemTypeName">The content type of the rows in that bag, or empty for the core.</param>
/// <param name="FieldName">
/// The content field, or <see cref="LookupValueReconciliation.CoreNationality"/> for the one core
/// property that is bound.
/// </param>
public sealed record LookupBoundLocation(string SectionName, string ItemTypeName, string FieldName);

/// <summary>One value records hold that is in no entry of the list.</summary>
/// <param name="Value">The value exactly as it is stored.</param>
/// <param name="Count">How many rows hold it.</param>
/// <param name="SampleEmployeeCodes">
/// A few of the people it is on, so a reader can go and look at one rather than taking the number
/// on trust. Capped: the point is to recognise the data, not to list it.
/// </param>
public sealed record UnmatchedLookupValue(string Value, int Count, IReadOnlyList<string> SampleEmployeeCodes);

/// <summary>What a scan found.</summary>
/// <param name="EmployeesScanned">How many employee records were read.</param>
/// <param name="WasTruncated">
/// Whether the scan stopped at its cap. Said on the screen, because a reader shown "three values
/// not in the list" would otherwise take it for the whole answer.
/// </param>
/// <param name="Values">The unmatched values, most-used first.</param>
public sealed record LookupValueScan(
    int EmployeesScanned,
    bool WasTruncated,
    IReadOnlyList<UnmatchedLookupValue> Values)
{
    public static readonly LookupValueScan Empty = new(0, false, []);
}

/// <summary>
/// Finding the values records already hold that are in no entry of the list they are bound to, and
/// mapping them onto entries.
/// </summary>
/// <remarks>
/// <b>This is the half of "no data lost" that is deliberate.</b> The control is the other half: it
/// never drops a value it cannot match, so binding a list to a field that already holds free text
/// changes nothing about what is stored. That leaves a tenant with values the list does not know
/// about, which is exactly the mess the feature exists to clear up — and clearing it up is a
/// decision a person makes, record by value, not something a migration does on their behalf.
///
/// <b>Nothing here runs unless somebody asks.</b> The scan is behind a button, because it reads
/// every employee record in the tenant: there is no index over the values a bag's fields hold,
/// since a bag's rows are serialised inside their employee rather than stored as content items of
/// their own. An index would be a row per bound field per employee, written on every save, to serve
/// a screen somebody opens a handful of times — the module README records that trade and why
/// Reporting is the thing that would justify paying it.
/// </remarks>
public interface ILookupValueReconciliation
{
    /// <summary>Where this list's values are stored, from the live content definitions.</summary>
    Task<IReadOnlyList<LookupBoundLocation>> BoundLocationsAsync(
        string listCode,
        CancellationToken cancellationToken = default);

    /// <summary>The values records hold that this list has no entry for.</summary>
    Task<LookupValueScan> ScanAsync(string listCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rewrites every stored <paramref name="fromValue"/> to <paramref name="toCode"/>, and reports
    /// how many rows changed.
    /// </summary>
    /// <remarks>
    /// The one operation in this module that writes to many employees at once, and the only one
    /// that cannot be undone by repeating it — which is why the screen says so before it is pressed.
    /// </remarks>
    Task<RecordResult<int>> MapAsync(
        string listCode,
        string fromValue,
        string toCode,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc />
internal sealed class LookupValueReconciliation : ILookupValueReconciliation
{
    /// <summary>
    /// The field name standing for the employee core's nationality, which is not a content field.
    /// </summary>
    /// <remarks>
    /// <c>EmployeePart.NationalityCode</c> is a property on the fixed core, edited by this module's
    /// own form rather than by Orchard's content editor, so it has no content-definition entry to
    /// discover. It is named here instead — the one hard-coded location, because the core is fixed
    /// by design and a core field is not something a tenant can add.
    /// </remarks>
    public const string CoreNationality = "EmployeePart.NationalityCode";

    /// <summary>
    /// How many employee records one scan reads.
    /// </summary>
    /// <remarks>
    /// A cap rather than a page control, because this is a diagnostic rather than a list: the
    /// question is "what values are out there", and the answer stops being useful long before
    /// somebody pages through it. Two thousand covers every tenant the product has and is bounded
    /// work on one that is larger; the screen says when it stopped early so the number on it is
    /// never mistaken for the whole truth.
    /// </remarks>
    private const int MaxEmployeesScanned = 2_000;

    /// <summary>How many people are named against each value.</summary>
    private const int SampleSize = 5;

    private readonly ISession _session;
    private readonly IContentManager _contentManager;
    private readonly IContentDefinitionManager _contentDefinitionManager;
    private readonly ILookupListLookup _lists;

    /// <summary>
    /// How the one value on the employee core is written.
    /// </summary>
    /// <remarks>
    /// <b>Not by altering <c>EmployeePart</c> directly, which is what this did first and which did
    /// not work.</b> The core has a service that owns it, and CLAUDE.md rule 9 is explicit that an
    /// aggregate is reached through its own service rather than through its storage — the module
    /// README makes the same point about there being one way to create an employee. Going through
    /// <see cref="IEmployeeService"/> means the write is validated, raises
    /// <c>EmployeeChanged</c> and produces exactly the record an edit on the employee screen would.
    ///
    /// A section row has no equivalent: its rows are serialised inside the employee and are edited
    /// through Orchard's content editor, so those are written through <see cref="IContentManager"/>
    /// as the content editor itself does.
    /// </remarks>
    private readonly IEmployeeService _employees;

    private readonly IRecordsAuthorisation _authorisation;
    private readonly IAuditTrailManager _auditTrailManager;
    private readonly IStringLocalizer S;

    public LookupValueReconciliation(
        ISession session,
        IContentManager contentManager,
        IContentDefinitionManager contentDefinitionManager,
        ILookupListLookup lists,
        IEmployeeService employees,
        IRecordsAuthorisation authorisation,
        IAuditTrailManager auditTrailManager,
        IStringLocalizer<LookupValueReconciliation> stringLocalizer)
    {
        _session = session;
        _contentManager = contentManager;
        _contentDefinitionManager = contentDefinitionManager;
        _lists = lists;
        _employees = employees;
        _authorisation = authorisation;
        _auditTrailManager = auditTrailManager;
        S = stringLocalizer;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Read from the live content definitions rather than from
    /// <see cref="StandardLookupLists.Bindings"/>, so that a binding a tenant makes in a later
    /// slice is found by the same code. The standard bindings are how the fields get their settings
    /// in the first place; this is how anything reads them back.
    ///
    /// A pure read path, so <c>GetTypeDefinitionAsync</c> rather than <c>Load…</c> — ADR-0006: the
    /// cached read is wrong only inside a scope that has just changed a definition, and this scope
    /// never does.
    /// </remarks>
    public async Task<IReadOnlyList<LookupBoundLocation>> BoundLocationsAsync(
        string listCode,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var code = LookupCodes.Normalise(listCode);

        if (code.Length == 0)
        {
            return [];
        }

        var locations = new List<LookupBoundLocation>();

        // The one core property that is bound. Not discoverable, because it is not a content field.
        if (LookupCodes.Same(code, StandardLookupLists.Nationality))
        {
            locations.Add(new LookupBoundLocation(string.Empty, string.Empty, CoreNationality));
        }

        var employeeType = await _contentDefinitionManager.GetTypeDefinitionAsync(EmployeeFieldNames.ContentType);

        if (employeeType is null)
        {
            return locations;
        }

        // Every bag on the employee, and the one item type each holds. Walked from the definition
        // rather than from EmployeeSections.All so a section a tenant adds later is covered by the
        // same code — the stereotype exists for exactly this reason.
        foreach (var typePart in employeeType.Parts.Where(part => part.PartDefinition.Name == nameof(BagPart)))
        {
            var contained = typePart.GetSettings<BagPartSettings>().ContainedContentTypes ?? [];

            foreach (var itemTypeName in contained)
            {
                var partDefinition = await _contentDefinitionManager.GetPartDefinitionAsync(itemTypeName);

                if (partDefinition is null)
                {
                    continue;
                }

                foreach (var field in partDefinition.Fields.Where(field => IsBoundTo(field, code)))
                {
                    locations.Add(new LookupBoundLocation(typePart.Name, itemTypeName, field.Name));
                }
            }
        }

        return locations;
    }

    /// <inheritdoc />
    public async Task<LookupValueScan> ScanAsync(string listCode, CancellationToken cancellationToken = default)
    {
        var list = await _lists.GetByCodeAsync(listCode, cancellationToken);

        if (list is null)
        {
            return LookupValueScan.Empty;
        }

        var locations = await BoundLocationsAsync(listCode, cancellationToken);

        if (locations.Count == 0)
        {
            return LookupValueScan.Empty;
        }

        // Every entry, retired included: a retired entry is a value a record may legitimately hold
        // and is not something to reconcile. Retiring is how an entry stops being offered, not how
        // it stops being valid.
        var known = new HashSet<string>(list.Items.Select(item => item.Code), LookupCodes.Comparer);

        var ids = await EmployeeIdsAsync(cancellationToken);
        var truncated = ids.Count > MaxEmployeesScanned;
        var scanned = truncated ? ids.Take(MaxEmployeesScanned).ToList() : ids;

        var found = new Dictionary<string, (int Count, List<string> Samples)>(LookupCodes.Comparer);

        foreach (var id in scanned)
        {
            var employee = await _contentManager.GetAsync(id);

            if (employee is null)
            {
                continue;
            }

            var employeeCode = employee.Get<EmployeePart>(nameof(EmployeePart))?.EmployeeCode ?? id;

            foreach (var value in ValuesIn(employee, locations))
            {
                if (value.Length == 0 || known.Contains(value))
                {
                    continue;
                }

                if (!found.TryGetValue(value, out var tally))
                {
                    tally = (0, []);
                }

                if (tally.Samples.Count < SampleSize &&
                    !tally.Samples.Contains(employeeCode, StringComparer.Ordinal))
                {
                    tally.Samples.Add(employeeCode);
                }

                found[value] = (tally.Count + 1, tally.Samples);
            }
        }

        var values = found
            // Most-used first: the value on four hundred records is the one worth deciding about,
            // and the one typed once by mistake can wait.
            .OrderByDescending(entry => entry.Value.Count)
            .ThenBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
            .Select(entry => new UnmatchedLookupValue(entry.Key, entry.Value.Count, entry.Value.Samples))
            .ToList();

        return new LookupValueScan(scanned.Count, truncated, values);
    }

    /// <inheritdoc />
    public async Task<RecordResult<int>> MapAsync(
        string listCode,
        string fromValue,
        string toCode,
        CancellationToken cancellationToken = default)
    {
        if (!await _authorisation.AuthoriseAsync(Permissions.ManageLookupLists))
        {
            return RecordResult.NotAuthorised<int>();
        }

        var list = await _lists.GetByCodeAsync(listCode, cancellationToken);

        if (list is null)
        {
            return RecordResult.Failed<int>(new RecordError(
                RecordRule.UnknownLookupList,
                listCode,
                S["There is no lookup list with the code '{0}' in this tenant.", listCode]));
        }

        var from = LookupCodes.Normalise(fromValue);
        var to = LookupCodes.Normalise(toCode);

        if (from.Length == 0)
        {
            return RecordResult.Failed<int>(new RecordError(
                RecordRule.CodeFormat,
                from,
                S["There is no value to map."]));
        }

        var target = list.Items.FirstOrDefault(item => LookupCodes.Same(item.Code, to));

        if (target is null)
        {
            return RecordResult.Failed<int>(new RecordError(
                RecordRule.UnknownLookupItem,
                to,
                S["'{0}' is not an entry of the list '{1}', so nothing can be mapped onto it.", to, list.Code]));
        }

        // Mapping a value that is already an entry would rewrite records that are not wrong. The
        // screen never offers it; a hand-made request is refused here.
        if (list.Items.Any(item => LookupCodes.Same(item.Code, from)))
        {
            return RecordResult.Failed<int>(new RecordError(
                RecordRule.ImmutableOnceInUse,
                from,
                S["'{0}' is already an entry of this list, so the records holding it are not unmatched and are left alone.", from]));
        }

        var locations = await BoundLocationsAsync(listCode, cancellationToken);

        if (locations.Count == 0)
        {
            return RecordResult.Success(0);
        }

        var ids = await EmployeeIdsAsync(cancellationToken);

        var changedRows = 0;
        var changedEmployees = new List<string>();

        var sectionLocations = locations.Where(location => location.FieldName != CoreNationality).ToList();
        var rewritesTheCore = locations.Count != sectionLocations.Count;

        foreach (var id in ids.Take(MaxEmployeesScanned))
        {
            var employee = await _contentManager.GetAsync(id);

            if (employee is null)
            {
                continue;
            }

            var core = employee.Get<EmployeePart>(nameof(EmployeePart));
            var rows = RewriteSections(employee, sectionLocations, from, target.Code);

            if (rows > 0)
            {
                // Through IContentManager, so this module's own handlers run and the item is
                // written the way the content editor itself writes it. See
                // WorkMate.Dimensions.Migrations.UpdateFrom6Async for why a direct
                // ISession.SaveAsync would be the wrong tool for a content item.
                await _contentManager.UpdateAsync(employee);
            }

            if (rewritesTheCore && core is not null && LookupCodes.Same(core.NationalityCode, from))
            {
                // The core goes through its own service rather than through the content item —
                // see the remark on _employees for why altering the part directly is both wrong
                // and, as it turned out, ineffective.
                var updated = await _employees.UpdateAsync(
                    id,
                    new WorkMate.Core.BilingualText(core.NameEn, core.NameAr),
                    new EmployeeDetails(
                        core.DateOfBirth,
                        target.Code,
                        core.Gender,
                        string.IsNullOrEmpty(core.LineManagerEmployeeId) ? null : core.LineManagerEmployeeId,
                        core.PhotoPath),
                    cancellationToken);

                if (!updated.Succeeded)
                {
                    return RecordResult.Failed<int>(updated.Errors);
                }

                rows++;
            }

            if (rows == 0)
            {
                continue;
            }

            changedRows += rows;
            changedEmployees.Add(core?.EmployeeCode ?? id);
        }

        await RecordAsync(list.Code, from, target.Code, changedRows, changedEmployees);

        return RecordResult.Success(changedRows);
    }

    /// <summary>
    /// Every employee's content item id, newest-indexed first, capped one past the scan limit so
    /// truncation is detectable.
    /// </summary>
    /// <remarks>
    /// <b>Ids from the index, then each item through <see cref="IContentManager"/> — not content
    /// items straight out of a session query.</b> That shortcut was written first and was wrong in
    /// a way worth recording, because it looked like it worked: a query-materialised
    /// <c>ContentItem</c> and the one <c>IContentManager.GetAsync</c> hands back are two different
    /// objects for the same document, and YesSql requires a tracked document to be mutated through
    /// the instance it is tracking. The core's write went through <c>IEmployeeService</c>, which
    /// loads its own instance — so the update succeeded, reported success, and was then undone by
    /// the stale instance this loop was still holding. Nothing threw.
    ///
    /// <c>Migrations.UpdateFrom1Async</c> takes the same route for the same reason, and says so.
    /// </remarks>
    private async Task<List<string>> EmployeeIdsAsync(CancellationToken cancellationToken) =>
    [
        .. (await _session
                .QueryIndex<EmployeeIndex>(index => index.Latest)
                .Take(MaxEmployeesScanned + 1)
                .ListAsync(cancellationToken))
            .Select(row => row.ContentItemId)
            .Distinct(StringComparer.Ordinal),
    ];

    /// <summary>Whether a field's settings bind it to this list.</summary>
    private static bool IsBoundTo(ContentPartFieldDefinition field, string listCode) =>
        string.Equals(field.Editor(), LookupFieldEditor.Name, StringComparison.Ordinal) &&
        LookupCodes.Same(field.GetSettings<LookupFieldSettings>().ListCode, listCode);

    /// <summary>Every value this employee holds at the bound locations.</summary>
    private static IEnumerable<string> ValuesIn(
        ContentItem employee,
        IReadOnlyList<LookupBoundLocation> locations)
    {
        foreach (var location in locations)
        {
            if (location.FieldName == CoreNationality)
            {
                yield return LookupCodes.Normalise(employee.Get<EmployeePart>(nameof(EmployeePart))?.NationalityCode);

                continue;
            }

            var bag = employee.Get<BagPart>(location.SectionName);

            if (bag is null)
            {
                continue;
            }

            foreach (var row in bag.ContentItems.Where(
                row => string.Equals(row.ContentType, location.ItemTypeName, StringComparison.Ordinal)))
            {
                yield return LookupCodes.Normalise(TextOf(row, location));
            }
        }
    }

    /// <summary>
    /// Rewrites every bound <em>section</em> row on this employee holding <paramref name="from"/>,
    /// and returns how many changed. The core is the caller's business.
    /// </summary>
    /// <remarks>
    /// A bag's rows are serialised inside the employee rather than stored as content items of their
    /// own, so the mutation has to go back through <c>Alter</c> on the employee: changing a
    /// materialised row in place would change an object the employee's JSON does not point at.
    /// </remarks>
    private static int RewriteSections(
        ContentItem employee,
        IReadOnlyList<LookupBoundLocation> locations,
        string from,
        string to)
    {
        var changed = 0;

        foreach (var location in locations)
        {
            if (employee.Get<BagPart>(location.SectionName) is null)
            {
                continue;
            }

            var rowsHere = 0;

            employee.Alter<BagPart>(location.SectionName, bag =>
            {
                foreach (var row in bag.ContentItems.Where(
                    row => string.Equals(row.ContentType, location.ItemTypeName, StringComparison.Ordinal)))
                {
                    if (!LookupCodes.Same(TextOf(row, location), from))
                    {
                        continue;
                    }

                    row.Alter<ContentPart>(location.ItemTypeName, part =>
                        part.Alter<TextField>(location.FieldName, field => field.Text = to));

                    rowsHere++;
                }
            });

            changed += rowsHere;
        }

        return changed;
    }

    /// <summary>
    /// The text a bound field holds on one bag row.
    /// </summary>
    /// <remarks>
    /// The part carrying a section's fields is named for the section's content type, which is the
    /// convention <c>Migrations.SectionTypeAsync</c> follows and Orchard's own type editor does too.
    /// </remarks>
    private static string? TextOf(ContentItem row, LookupBoundLocation location) =>
        row.Get<ContentPart>(location.ItemTypeName)?.Get<TextField>(location.FieldName)?.Text;

    /// <summary>
    /// One audit entry for the whole remap, rather than one per employee.
    /// </summary>
    /// <remarks>
    /// A bulk remap is a configuration decision, not an edit to each person: "every record that
    /// said 'Grade One' now says G1" is one thing somebody did, and four hundred
    /// <c>EmployeeChanged</c> entries saying the same thing would bury it and everything around it.
    /// The affected employee codes travel on the entry — capped, because the payload is read by a
    /// person — so an auditor can still get from the decision to the records.
    /// </remarks>
    private Task RecordAsync(
        string listCode,
        string from,
        string to,
        int rows,
        List<string> employeeCodes) =>
        _auditTrailManager.RecordEventAsync(new AuditTrailContext<LookupValuesRemappedAuditEvent>(
            FormsAuditTrail.LookupValuesRemapped,
            FormsAuditTrail.Category,
            listCode,
            userId: null,
            userName: null,
            new LookupValuesRemappedAuditEvent
            {
                ListCode = listCode,
                FromValue = from,
                ToCode = to,
                RowsChanged = rows,
                EmployeeCodes = [.. employeeCodes.Take(50)],
                EmployeesChanged = employeeCodes.Count,
            }));
}
