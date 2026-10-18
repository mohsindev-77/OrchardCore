using WorkMate.Core;

namespace WorkMate.Records.Models;

/// <summary>
/// What a customer asked their employee record to look like. Slice B3, decision D33.
/// </summary>
/// <remarks>
/// <b>This is the source of truth; the content type is the output.</b> Design note §5.1: the
/// content definition Orchard ends up with is the <em>result of applying</em> this document —
/// derived, regenerable, and never the thing anybody exports or hand-edits. If the two disagree,
/// this wins and republishing makes it so. That split is what lets B6 export a tenant's forms as
/// definitions and have them still apply after an Orchard upgrade that changes how content types
/// are stored.
///
/// <b>Orchard's own content-type editor can still reach the generated types</b>, and that cannot
/// be taken away. The mitigation is the direction of truth above: a hand edit survives until the
/// next publish and then loses.
///
/// <b>ADR-0007.</b> No settable property here defaults to a shared static: bilingual names are
/// <c>new(string.Empty, string.Empty)</c> and collections are <c>[]</c>, which for an
/// interface-typed property compiles to <c>Array.Empty&lt;T&gt;()</c> — shared, but replaceable
/// only wholesale. <c>SharedStaticDocumentDefaultTests</c> scans this file the day it exists.
/// </remarks>
public sealed class FormDefinitionDocument
{
    /// <summary>YesSql's own identity. Never ours to read.</summary>
    public long Id { get; set; }

    /// <summary>
    /// The concurrency stamp. Written through <c>ISession.SaveCheckedAsync</c> like every other
    /// document in this module — <c>ConcurrencyCheckedSaveTests</c> fails the build otherwise.
    /// </summary>
    public long Version { get; set; }

    /// <summary>The generated identifier this definition is referred to by within the tenant.</summary>
    public string FormDefinitionId { get; set; } = string.Empty;

    /// <summary>
    /// The natural key: immutable, and what a recipe and an export name this definition by.
    /// </summary>
    /// <remarks>
    /// By code rather than by the generated id, for the reason every other reference on this
    /// platform is: an id means nothing in the tenant a recipe lands in.
    /// </remarks>
    public string Code { get; set; } = string.Empty;

    /// <summary>What a reader sees. Bilingual at definition time — specification section 5.</summary>
    public BilingualText Name { get; set; } = new(string.Empty, string.Empty);

    /// <summary>What this definition adds fields to. Decision D33, Q1.</summary>
    public FormTarget Target { get; set; }

    /// <summary>
    /// The section this definition is about, when <see cref="Target"/> is
    /// <see cref="FormTarget.Section"/> or <see cref="FormTarget.NewSection"/>.
    /// </summary>
    /// <remarks>
    /// For <see cref="FormTarget.Section"/> it is one of the six the product ships — the item type
    /// name, <c>EmployeeJobDetail</c> and so on — or one a tenant added earlier. For
    /// <see cref="FormTarget.NewSection"/> it is the item type name to create, and the bag welded
    /// onto <c>Employee</c> is named by <see cref="SectionName"/>.
    ///
    /// Empty for <see cref="FormTarget.Record"/>, which is always <c>EmployeeProfilePart</c>.
    /// </remarks>
    public string SectionItemType { get; set; } = string.Empty;

    /// <summary>
    /// The bag's name on <c>Employee</c> for a new section — <c>Assets</c>, <c>Training</c>.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="SectionItemType"/> for the same reason the six shipped sections
    /// distinguish them: the bag is what the employee page loops over and the item type is what
    /// one row of it is. <c>EmployeeSections.All</c> pairs them for the shipped six.
    /// </remarks>
    public string SectionName { get; set; } = string.Empty;

    /// <summary>The section's own label, for the employee page's heading. Bilingual.</summary>
    public BilingualText SectionLabel { get; set; } = new(string.Empty, string.Empty);

    /// <summary>The fields, in the order a reader is offered them.</summary>
    /// <remarks>
    /// Order is part of the definition and is compiled into
    /// <c>ContentPartFieldSettings.Position</c>, so Orchard's own generic editors honour it too —
    /// design note §2.3.
    /// </remarks>
    public IReadOnlyList<FormFieldDefinition> Fields { get; set; } = [];

    /// <summary>
    /// The version number most recently published, or zero while this has never been published.
    /// </summary>
    public int PublishedVersion { get; set; }

    /// <summary>
    /// Whether the draft differs from what was last published.
    /// </summary>
    /// <remarks>
    /// Kept on the document rather than computed by comparing against the version document,
    /// because the designer's list shows it per row and a comparison per row is a read per row.
    /// It is set by every edit and cleared by a publish, which are the only two things that can
    /// change the answer.
    /// </remarks>
    public bool HasUnpublishedChanges { get; set; }
}

/// <summary>What a form definition adds fields to. Decision D33, Q1.</summary>
/// <remarks>
/// Three targets, which is the whole of Q1's answer. A fourth — <c>Request</c>, a form of its own
/// with <c>FormSubmissionPart</c> — arrives in slice B7 and is deliberately not declared here:
/// an enum member nothing writes and nothing reads is a promise the model has not made.
/// </remarks>
public enum FormTarget
{
    /// <summary>
    /// Fields on one of the six standard sections, or one the tenant added earlier.
    /// </summary>
    /// <remarks>
    /// The common case, and the one with data already behind it — possibly a lot of it, which is
    /// why the safety rules matter most here.
    /// </remarks>
    Section,

    /// <summary>
    /// A section of the tenant's own: a new item type, and a new bag welded onto <c>Employee</c>.
    /// </summary>
    /// <remarks>
    /// Exactly what <c>Migrations.SectionTypeAsync</c> does for the six the product ships — the
    /// stereotype, not creatable, not listable, a generated title — which is why the compiler
    /// reuses that shape rather than inventing a second one.
    /// </remarks>
    NewSection,

    /// <summary>
    /// Fields on the employee record itself, held by <c>EmployeeProfilePart</c>. Q2.
    /// </summary>
    /// <remarks>
    /// Not something the specification asks for; the design note proposed it and Q2 accepted it.
    /// A tenant wanting one more fact about a person — a staff-card number, a locker — should not
    /// have to invent a one-row section to hold it.
    /// </remarks>
    Record,
}

/// <summary>
/// One field a customer asked for: what it is, what it is called, and whether it is still offered.
/// </summary>
/// <remarks>
/// <b>A record rather than a class with setters</b>, so a field in a published version cannot be
/// written into by anything holding a reference to it. ADR-0007's hazard does not arise: every
/// instance is constructed fresh.
/// </remarks>
/// <param name="Name">
/// The content-definition field name, which is what Liquid, GraphQL, the API and every report
/// address it by. <b>Immutable once published</b> — safety rule 3 — because renaming it orphans
/// every value already stored under the old name.
/// </param>
/// <param name="Type">
/// The designer type, from the closed palette. <b>Immutable once published</b> — safety rule 2.
/// </param>
/// <param name="Label">What a reader sees. Changes freely: that is what a label is for.</param>
/// <param name="Help">The line under the box. Changes freely.</param>
/// <param name="IsRequired">
/// Whether a save is refused without it. Turning it on is allowed and does not destroy data,
/// though it can make existing rows invalid — design note §5.3; B4's publish screen shows the
/// count that would fail.
/// </param>
/// <param name="IsRetired">
/// Whether the field has been taken off the editor. Safety rule 4: the data stays exactly where
/// it is, the field stays in the content definition, and un-retiring is the exact inverse.
/// </param>
/// <param name="ListCode">
/// The managed lookup list a <see cref="FormFieldType.Choice"/> offers. Empty for every other
/// type. Design note §3.3: a choice is a <c>TextField</c> storing a code with a different editor,
/// which is what makes turning free text into a choice a settings change rather than a migration.
/// </param>
public sealed record FormFieldDefinition(
    string Name,
    FormFieldType Type,
    BilingualText Label,
    BilingualText Help,
    bool IsRequired = false,
    bool IsRetired = false,
    string ListCode = "");

/// <summary>
/// The closed palette of field types a designer may choose. Design note §3.2.
/// </summary>
/// <remarks>
/// <b>Closed on purpose.</b> Every type here is something the whole platform — reports, the API,
/// payroll, the approval engine — has to be able to read and reason about, so "any field type
/// installed on the tenant" is not an option. The dimension engine's attribute schema took the
/// same decision for the same reason.
///
/// <b>Nothing here points at anything that does not already have its own service and its own
/// dated model</b>, which is what keeps §9's position question open: a designer field can never
/// become a second, undated answer to "where does this person sit".
///
/// The types the design note lists as later — the dimension picker, money, dual-calendar date,
/// the attachment set — are deliberately absent rather than declared and refused.
/// </remarks>
public enum FormFieldType
{
    /// <summary>One line of text. Orchard's <c>TextField</c>.</summary>
    Text,

    /// <summary>Several lines. The same <c>TextField</c> with Orchard's text-area editor.</summary>
    MultilineText,

    /// <summary>English and Arabic together. <c>BilingualTextField</c>, ADR-0003.</summary>
    BilingualText,

    /// <summary>A number. Orchard's <c>NumericField</c>.</summary>
    Number,

    /// <summary>A date. Orchard's <c>DateField</c>.</summary>
    Date,

    /// <summary>Yes or no. Orchard's <c>BooleanField</c>.</summary>
    YesNo,

    /// <summary>
    /// One of a managed list's entries, stored as its code. <c>TextField</c> + the lookup editor.
    /// </summary>
    /// <remarks>
    /// <b>Not a new field type, which is the decision that makes migration free</b> — design note
    /// §3.3. A retired list is not offered for a new choice field (D22 addendum): the question it
    /// answered is answered by a dimension now.
    /// </remarks>
    Choice,

    /// <summary>Somebody on this tenant. Orchard's <c>ContentPickerField</c>, scoped to employees.</summary>
    EmployeePicker,

    /// <summary>A file. Orchard's <c>MediaField</c>, plain attachment only.</summary>
    File,
}
