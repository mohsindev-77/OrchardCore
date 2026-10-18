using WorkMate.Core;
using WorkMate.Records.Models;

namespace WorkMate.Records.Services;

/// <summary>
/// The form designer's one aggregate: definitions, their versions, and publishing them.
/// Slice B3, decision D33.
/// </summary>
/// <remarks>
/// <b>One aggregate, one service</b> — CLAUDE.md rule 9. The designer screens B4 builds, B6's
/// recipe step and any later API all go through here, so there is no privileged path and no
/// second place a safety rule could be enforced differently. That matters more here than almost
/// anywhere else in the product: the rules this service keeps are the ones standing between an
/// administrator and their own data.
///
/// <b>The definition is the source of truth and the content type is the output.</b> Design note
/// §5.1. <see cref="PublishAsync"/> rebuilds the content definition from the document every time,
/// which means a hand edit made on Orchard's own content-type editor survives until the next
/// publish and then loses. That is the right direction and it is said out loud in the README so
/// nobody is surprised by it.
///
/// <b>Publishing is additive and never deletes.</b> No path here removes a field from a content
/// definition, removes a part from <c>Employee</c>, or deletes a stored value. Taking a field out
/// of a definition hides it; the data stays exactly where it is. That is a property of the
/// compiler rather than a rule somebody remembers — there is no code that could delete.
///
/// <b>ADR-0006 governs every definition read on this path.</b> Publishing alters content
/// definitions, so it reads them with <c>Load…</c> and never <c>Get…</c>, and
/// <c>EmployeeHandler.ActivatingAsync</c> carries the repair for the case this service makes
/// possible: a recipe publishing a form and then creating employees in the same shell scope.
/// </remarks>
public interface IFormDefinitionService
{
    /// <summary>The definition with this generated id, or null.</summary>
    Task<FormDefinitionDocument?> GetAsync(
        string formDefinitionId,
        CancellationToken cancellationToken = default);

    /// <summary>The definition with this code, or null. How a recipe and an export name one.</summary>
    Task<FormDefinitionDocument?> GetByCodeAsync(
        string code,
        CancellationToken cancellationToken = default);

    /// <summary>Every definition this tenant has, by English name.</summary>
    /// <remarks>
    /// Unpaged, like <c>ILookupListService.ListAsync</c> and for the same reason: a tenant with
    /// more form definitions than a screen can show has a problem a page size will not fix.
    /// </remarks>
    Task<IReadOnlyList<FormDefinitionDocument>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts a definition. Nothing is compiled and no content type changes until a publish.
    /// </summary>
    /// <remarks>
    /// <b>A draft is cheap and a publish is not</b>, which is why they are two operations. The
    /// target is fixed at creation: moving a definition from one section to another would mean
    /// the fields it has already published are on a part this definition no longer claims, and
    /// nothing good follows from that.
    /// </remarks>
    /// <param name="target">What this adds fields to. Immutable afterwards.</param>
    /// <param name="sectionItemType">
    /// The item type for a <see cref="FormTarget.Section"/> or <see cref="FormTarget.NewSection"/>
    /// definition. Ignored for <see cref="FormTarget.Record"/>.
    /// </param>
    /// <param name="sectionName">
    /// The bag's name on <c>Employee</c> for a new section. Ignored otherwise.
    /// </param>
    Task<RecordResult<FormDefinitionDocument>> CreateAsync(
        string code,
        BilingualText name,
        FormTarget target,
        string? sectionItemType = null,
        string? sectionName = null,
        BilingualText? sectionLabel = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// States the draft's whole field list, in order. Nothing is compiled until a publish.
    /// </summary>
    /// <remarks>
    /// <b>The whole set, not a delta</b> — the same shape the placement screen uses for a split
    /// and <c>ILookupListService.UpdateAsync</c> uses for a list's entries, and for the same
    /// reason: "these are the fields, in this order" is a statement about the set, and applying it
    /// one field at a time would be invalid at every step but the last.
    ///
    /// <b>What this refuses</b> is safety rules 2 and 3 — a published field whose type or name has
    /// changed. What it allows is everything that does not destroy data: adding, reordering,
    /// relabelling, requiring, retiring, un-retiring, and taking a field out of the list
    /// altogether, which hides it and keeps every value already stored in it.
    /// </remarks>
    Task<RecordResult<FormDefinitionDocument>> SetFieldsAsync(
        string formDefinitionId,
        IReadOnlyList<FormFieldDefinition> fields,
        CancellationToken cancellationToken = default);

    /// <summary>Renames a definition. Labels are presentation and change freely.</summary>
    Task<RecordResult<FormDefinitionDocument>> RenameAsync(
        string formDefinitionId,
        BilingualText name,
        BilingualText? sectionLabel = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks a draft against every rule a publish would, and changes nothing.
    /// </summary>
    /// <remarks>
    /// The dry run the organisation designer's move, merge and exit screens all have. B4's publish
    /// screen calls this to show what would be refused before anybody commits to it — and the
    /// service calls it itself inside <see cref="PublishAsync"/>, so the preview and the result
    /// cannot disagree.
    /// </remarks>
    Task<IReadOnlyList<RecordError>> ValidateAsync(
        string formDefinitionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Compiles the definition into content definitions and writes a version. Decision D33.
    /// </summary>
    /// <remarks>
    /// <b>What it does, in order.</b> Validates; refuses on any error, changing nothing. Then
    /// alters the part — creating it, the item type and the bag on <c>Employee</c> for a new
    /// section — adding every field the definition has and leaving every field it does not.
    /// Then writes an immutable <c>FormVersionDocument</c>, stamps the definition's published
    /// version, clears its unpublished-changes flag, files an audit entry, and signals the caches
    /// after the commit (ADR-0019).
    ///
    /// <b>Publishing an unchanged definition is a no-op.</b> It writes no version, files no audit
    /// entry and allocates no version number, because a version is a record of a change and a
    /// history full of identical entries is a history nobody reads. The result says so rather than
    /// failing: asking twice is not an error, and a recipe applied twice (ADR-0008) relies on it.
    ///
    /// <b>Nothing is ever deleted.</b> A field taken out of the definition stays in the content
    /// definition and keeps its data; it simply stops being rendered. There is no code path here
    /// that removes a field, a part or a value.
    /// </remarks>
    Task<RecordResult<FormPublishResult>> PublishAsync(
        string formDefinitionId,
        CancellationToken cancellationToken = default);

    /// <summary>Every published version of a definition, newest first.</summary>
    Task<IReadOnlyList<FormVersionDocument>> GetVersionsAsync(
        string code,
        CancellationToken cancellationToken = default);
}

/// <summary>What a publish did. Slice B3.</summary>
/// <param name="Definition">The definition as it now stands.</param>
/// <param name="Version">
/// The version written, or null when the definition had not changed and nothing was written.
/// </param>
/// <param name="WasAlreadyPublished">
/// True when the draft matched the last published version and this call changed nothing.
/// </param>
public sealed record FormPublishResult(
    FormDefinitionDocument Definition,
    FormVersionDocument? Version,
    bool WasAlreadyPublished);
