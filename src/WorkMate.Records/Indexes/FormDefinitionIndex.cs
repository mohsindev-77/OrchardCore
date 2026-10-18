using WorkMate.Records.Models;
using WorkMate.Records.Services;
using YesSql.Indexes;

namespace WorkMate.Records.Indexes;

/// <summary>
/// The columns the form designer's list and the compiler query definitions by. Slice B3.
/// </summary>
/// <remarks>
/// CLAUDE.md rule 2: every custom table has an index class and a provider, and no read goes
/// through raw SQL. The shape follows <c>LookupListIndex</c>'s — the natural key folded for
/// comparison, and the few counts a list column shows — because the two screens are the same
/// screen with different rows in it.
/// </remarks>
public sealed class FormDefinitionIndex : MapIndex
{
    /// <summary>The generated id, for the lookup every other read starts from.</summary>
    public string FormDefinitionId { get; set; } = string.Empty;

    /// <summary>The natural key, as authored.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// The same code folded, which is what the uniqueness check actually compares.
    /// </summary>
    /// <remarks>
    /// Folded here rather than trusting the database's collation, for the reason
    /// <c>LookupListIndex.CodeUpper</c> and <c>EmployeeIndex.CodeUpper</c> both give: a provider
    /// whose default collation is case-sensitive and one whose default is not would disagree
    /// about whether two tenants' codes collide, and the disagreement would show up as a
    /// duplicate nobody could explain.
    /// </remarks>
    public string CodeUpper { get; set; } = string.Empty;

    /// <summary>The English name, for ordering a list without loading every document.</summary>
    public string NameEn { get; set; } = string.Empty;

    /// <summary>What it adds fields to, stored as its name so a reader of the table can tell.</summary>
    public string Target { get; set; } = string.Empty;

    /// <summary>
    /// The item type it is about, or empty for a record-level definition.
    /// </summary>
    /// <remarks>
    /// Indexed because the compiler asks "is another definition already targeting this section"
    /// on every publish — two definitions adding fields to <c>EmployeeJobDetail</c> is legitimate,
    /// but two <em>creating</em> it is not, and that is a query rather than a scan.
    /// </remarks>
    public string SectionItemType { get; set; } = string.Empty;

    /// <summary>The version most recently published, or zero while never published.</summary>
    public int PublishedVersion { get; set; }

    /// <summary>Whether the draft differs from what was last published.</summary>
    public bool HasUnpublishedChanges { get; set; }

    /// <summary>How many fields it declares, retired ones included.</summary>
    public int FieldCount { get; set; }
}

public sealed class FormDefinitionIndexProvider : IndexProvider<FormDefinitionDocument>
{
    public override void Describe(DescribeContext<FormDefinitionDocument> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.For<FormDefinitionIndex>()
            .Map(document => new FormDefinitionIndex
            {
                FormDefinitionId = document.FormDefinitionId,
                Code = document.Code,
                CodeUpper = FormCodes.Normalise(document.Code).ToUpperInvariant(),
                NameEn = document.Name.En,
                Target = document.Target.ToString(),
                SectionItemType = document.SectionItemType,
                PublishedVersion = document.PublishedVersion,
                HasUnpublishedChanges = document.HasUnpublishedChanges,
                FieldCount = document.Fields.Count,
            });
    }
}
