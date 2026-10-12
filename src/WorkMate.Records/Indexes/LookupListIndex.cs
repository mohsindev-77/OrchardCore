using WorkMate.Records.Models;
using WorkMate.Records.Services;
using YesSql.Indexes;

namespace WorkMate.Records.Indexes;

/// <summary>
/// The queryable shape of a <see cref="LookupListDocument"/>: enough to resolve a list by id or
/// code, to answer whether a code is already taken, and to draw the list of lists with its counts,
/// without loading every document and its entries.
/// </summary>
/// <remarks>
/// The counts are on the index rather than computed from <c>Items</c> at read time because the
/// screen that shows them shows every list at once: a tenant with a dozen lists of fifty entries
/// would otherwise deserialise six hundred entries to render twelve numbers.
/// </remarks>
public sealed class LookupListIndex : MapIndex
{
    /// <summary>The stable identity the admin screens route by.</summary>
    public string LookupListId { get; set; } = string.Empty;

    /// <summary>The natural key, as the customer capitalised it.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// The code folded to upper case, which is what the uniqueness check actually compares.
    /// </summary>
    /// <remarks>
    /// A second column rather than a case-insensitive comparison in the query, for the reason
    /// <c>EmployeeIndex.CodeUpper</c> sets out at length: collation is a property of the database
    /// and not of this code, so the same uniqueness query would admit <c>Grade</c> alongside
    /// <c>grade</c> on SQLite and refuse it on SQL Server. Folding here moves the decision into
    /// code, where it is the same everywhere and is testable.
    /// </remarks>
    public string CodeUpper { get; set; } = string.Empty;

    /// <summary>Indexed so the list of lists can sort by name without loading the documents.</summary>
    public string NameEn { get; set; } = string.Empty;

    /// <summary>Carried so an Arabic reader's list sorts and reads the same way.</summary>
    public string NameAr { get; set; } = string.Empty;

    /// <summary>True for the lists WorkMate ships, which cannot be deleted.</summary>
    public bool IsSystemDefined { get; set; }

    /// <summary>How many entries the list holds, retired ones included.</summary>
    public int ItemCount { get; set; }

    /// <summary>
    /// How many of those are still offered.
    /// </summary>
    /// <remarks>
    /// Both numbers, not one and a subtraction, because the list screen shows both and because
    /// "twelve entries, four still offered" is the sentence somebody reads before deciding whether
    /// a list needs attention.
    /// </remarks>
    public int ActiveItemCount { get; set; }
}

/// <summary>Maps each lookup list document onto its one index row.</summary>
public sealed class LookupListIndexProvider : IndexProvider<LookupListDocument>
{
    public override void Describe(DescribeContext<LookupListDocument> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.For<LookupListIndex>()
            .Map(document => new LookupListIndex
            {
                LookupListId = document.LookupListId,
                Code = document.Code,
                CodeUpper = LookupCodes.Normalise(document.Code).ToUpperInvariant(),
                NameEn = document.Name.En,
                NameAr = document.Name.Ar,
                IsSystemDefined = document.IsSystemDefined,
                ItemCount = document.Items.Count,
                ActiveItemCount = document.Items.Count(item => item.IsActive),
            });
    }
}
