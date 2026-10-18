using WorkMate.Core;

namespace WorkMate.Records.Models;

/// <summary>
/// One published version of a form definition, frozen. Slice B3, decision D33 (Q5).
/// </summary>
/// <remarks>
/// <b>Why a separate document rather than a history list on the definition.</b> Design note §5.2:
/// a submission pins a version for ever (slice B7), and <em>a version a later publish could edit
/// is not a version</em>. Keeping the snapshots inside the working copy would mean every publish
/// rewriting the document that holds all of history, which is exactly the shape that loses it.
///
/// <b>Employee records are not pinned to a version</b> — Q5. The employee record's shape is
/// additive, so an employee written against version 2 still reads correctly against version 5:
/// every field version 2 had is still there. What the versions are for is audit and export —
/// "what did this form look like when that record was keyed in" — and that question is answered
/// by reading a version, not by stamping one on two hundred thousand rows. Request forms are the
/// case that does pin, because a submission is a statement made against the form as it was, and
/// that is slice B7's to build.
///
/// <b>Immutable once written.</b> Nothing updates a version document; a publish writes a new one.
/// The only property Orchard-side that moves is YesSql's own <see cref="Id"/>.
///
/// <b>ADR-0007.</b> Bilingual names default to <c>new(string.Empty, string.Empty)</c> and
/// collections to <c>[]</c>; no settable property defaults to a shared static instance.
/// </remarks>
public sealed class FormVersionDocument
{
    /// <summary>YesSql's own identity. Never ours to read.</summary>
    public long Id { get; set; }

    /// <summary>The concurrency stamp, for <c>SaveCheckedAsync</c> like every other document here.</summary>
    public long Version { get; set; }

    /// <summary>The definition this is a version of, by its immutable code.</summary>
    /// <remarks>
    /// By code rather than by the generated id, so an exported version still names its definition
    /// in the tenant a recipe lands in — the same rule <see cref="FormDefinitionDocument.Code"/>
    /// follows and for the same reason.
    /// </remarks>
    public string FormCode { get; set; } = string.Empty;

    /// <summary>Which version this is: 1 for the first publish, and up by one each real publish.</summary>
    /// <remarks>
    /// <b>Per real publish.</b> Publishing a definition that has not changed writes nothing and
    /// allocates no number — see <c>IFormDefinitionService.PublishAsync</c> — so a gap in these
    /// numbers would be a defect rather than an ordinary occurrence.
    /// </remarks>
    public int VersionNumber { get; set; }

    /// <summary>The definition's name as it stood. Bilingual.</summary>
    public BilingualText Name { get; set; } = new(string.Empty, string.Empty);

    /// <summary>What it targeted, as it stood.</summary>
    public FormTarget Target { get; set; }

    /// <summary>The item type it was about, as it stood.</summary>
    public string SectionItemType { get; set; } = string.Empty;

    /// <summary>The bag's name on <c>Employee</c>, as it stood.</summary>
    public string SectionName { get; set; } = string.Empty;

    /// <summary>The section's own label, as it stood.</summary>
    public BilingualText SectionLabel { get; set; } = new(string.Empty, string.Empty);

    /// <summary>Every field as it stood, in the order it stood in.</summary>
    public IReadOnlyList<FormFieldDefinition> Fields { get; set; } = [];

    /// <summary>When this version was published, in UTC.</summary>
    public DateTime PublishedOnUtc { get; set; }

    /// <summary>
    /// Who published it, by user name, or empty where the platform published it itself.
    /// </summary>
    /// <remarks>
    /// Empty for a recipe import or a migration, which act as the platform rather than as a
    /// person — the same thing <c>ISystemOperation</c> marks elsewhere in this module. The audit
    /// entry beside this carries the full actor either way; this is for the version list's own
    /// column, which has room for a name and not for an explanation.
    /// </remarks>
    public string PublishedBy { get; set; } = string.Empty;
}
