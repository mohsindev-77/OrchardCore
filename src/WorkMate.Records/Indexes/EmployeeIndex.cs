using OrchardCore.ContentManagement;
using WorkMate.Dimensions.Indexes;
using WorkMate.Records.Models;
using YesSql.Indexes;

namespace WorkMate.Records.Indexes;

/// <summary>
/// The queryable shape of an employee: resolve by code, list and search by name, filter by status,
/// without loading the content item.
/// </summary>
/// <remarks>
/// The same reasoning as <c>DimensionRecordPartIndex</c>: a tenant with five thousand employees
/// must not load five thousand content items to draw a list page, resolve a head's name for a
/// designer card, or answer whether a code is already taken.
///
/// Dates are stored as <c>DateTime</c> columns because YesSql 5.4.7 has no mapping for
/// <c>DateOnly</c> and the schema builder throws on one. The conversion goes through
/// <see cref="EffectiveDates"/>, the dimension engine's own, rather than a copy of it: ADR-0005
/// calls that "the one place a calendar date crosses into an index column and back", and a second
/// implementation would be a second open-ended sentinel and a second chance to get the round trip
/// wrong. The constraint is the database's, not that module's, and this module already depends on
/// it.
/// </remarks>
public sealed class EmployeeIndex : MapIndex
{
    /// <summary>The content item this row describes. What assignments and head appointments refer to.</summary>
    public string ContentItemId { get; set; } = string.Empty;

    /// <summary>The natural key, unique within the tenant including against leavers.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// The code folded to upper case, which is what a uniqueness check actually compares.
    /// </summary>
    /// <remarks>
    /// A second column rather than a case-insensitive comparison in the query, because collation is
    /// a property of the database and not of this code: SQLite's default <c>BINARY</c> collation is
    /// case-sensitive and SQL Server's usual one is not, so the same uniqueness query would admit
    /// <c>EMP-1</c> alongside <c>emp-1</c> on one customer's database and refuse it on another.
    /// Folding here moves the decision into code, where it is the same everywhere and is testable.
    /// <c>Code</c> keeps the customer's own capitalisation, because that is what they recognise.
    /// </remarks>
    public string CodeUpper { get; set; } = string.Empty;

    /// <summary>Indexed so the list can search and sort by name without loading the items.</summary>
    public string NameEn { get; set; } = string.Empty;

    /// <summary>Carried so an Arabic reader's search and an Arabic card both work the same way.</summary>
    public string NameAr { get; set; } = string.Empty;

    /// <summary>
    /// The lifecycle status, stored by name.
    /// </summary>
    /// <remarks>
    /// By name rather than by ordinal, so that adding a status to <see cref="EmploymentStatus"/>
    /// cannot renumber the stored value of every existing row. The column is what the list filters
    /// on and what "active headcount" counts.
    /// </remarks>
    public string Status { get; set; } = string.Empty;

    /// <summary>The first day <see cref="Status"/> was true; see <c>EmployeePart.StatusEffectiveFrom</c>.</summary>
    public DateTime StatusEffectiveFrom { get; set; }

    public DateTime JoinDate { get; set; }

    /// <summary>
    /// The first day this employee was an ex-employee, or <c>null</c> while they have not left.
    /// </summary>
    /// <remarks>
    /// Carried as its own nullable column rather than derived from <see cref="Status"/> and
    /// <see cref="StatusEffectiveFrom"/> at query time, because "everyone employed on this date" is
    /// a two-column range scan with it and a scan of the whole table without — and that question is
    /// asked by every headcount report, every payroll run and the employee picker.
    /// </remarks>
    public DateTime? ExitedFrom { get; set; }

    /// <summary>Who they report to, or empty. Indexed so a manager's reports are one seek.</summary>
    public string LineManagerEmployeeId { get; set; } = string.Empty;

    /// <summary>
    /// Whether this row describes the latest version of the item.
    /// </summary>
    /// <remarks>
    /// The <c>Employee</c> type is not draftable, so latest and published are the same thing today.
    /// The columns are here because the index provider maps every version Orchard hands it, and a
    /// uniqueness query that forgot to filter would report a record as a duplicate of its own
    /// earlier version.
    /// </remarks>
    public bool Latest { get; set; }

    public bool Published { get; set; }
}

/// <summary>
/// Maps every content item carrying <see cref="EmployeePart"/> onto one index row, and everything
/// else onto none.
/// </summary>
public sealed class EmployeeIndexProvider : IndexProvider<ContentItem>
{
    public override void Describe(DescribeContext<ContentItem> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.For<EmployeeIndex>()
            // Every content item on the tenant passes through here, including those of modules that
            // know nothing about this one. Filtering first keeps the map total, so it never has to
            // return a null row. TryGet rather than the obsolete As<TPart>, and never GetOrCreate,
            // which would weld an empty part onto every content item on the tenant just to ask
            // whether it has one.
            .When(item => item.TryGet<EmployeePart>(out _))
            .Map(item =>
            {
                var part = item.Get<EmployeePart>(nameof(EmployeePart))!;
                var code = Services.EmployeeCodes.Normalise(part.EmployeeCode);

                return new EmployeeIndex
                {
                    ContentItemId = item.ContentItemId,
                    Code = code,
                    CodeUpper = code.ToUpperInvariant(),
                    NameEn = part.NameEn,
                    NameAr = part.NameAr,
                    Status = part.Status.ToString(),
                    StatusEffectiveFrom = EffectiveDates.ToColumn(part.StatusEffectiveFrom),
                    JoinDate = EffectiveDates.ToColumn(part.JoinDate),
                    ExitedFrom = part.Status == EmploymentStatus.Exited
                        ? EffectiveDates.ToColumn(part.StatusEffectiveFrom)
                        : null,
                    LineManagerEmployeeId = part.LineManagerEmployeeId,
                    Latest = item.Latest,
                    Published = item.Published,
                };
            });
    }
}
