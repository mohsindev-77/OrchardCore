using WorkMate.Core;

namespace WorkMate.Dimensions.Services;

/// <summary>
/// The hierarchy: who is under whom, on which axis, on which date. The only way anything reads
/// the link or closure tables.
/// </summary>
/// <remarks>
/// Every read takes an effective date defaulting to today. There is no undated read path,
/// because an undated read path is how historical questions quietly start returning present-day
/// answers.
///
/// The maintenance operations are on this interface rather than hidden behind
/// <c>IDimensionService</c> because the record handler has to call them too: a record created
/// from Orchard's own content screens still needs its self pairs, and the handler and the
/// service share the ambient session so the record and its index rows commit together.
/// </remarks>
public interface IDimensionGraphService
{
    // ---- resolve ----------------------------------------------------------------------

    /// <summary>The node's ancestors on this axis, nearest first.</summary>
    Task<IReadOnlyList<DimensionNodeRef>> GetAncestorsAsync(
        string structureId,
        string recordId,
        DateOnly? asAt = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The node's descendants on this axis, excluding itself. Paged and never unbounded, per
    /// specification section 7.
    /// </summary>
    Task<Page<DimensionNodeRef>> GetDescendantsAsync(
        string structureId,
        string recordId,
        DateOnly? asAt = null,
        int skip = 0,
        int take = 50,
        CancellationToken cancellationToken = default);

    /// <summary>The node's immediate children on this axis.</summary>
    Task<IReadOnlyList<DimensionNodeRef>> GetChildrenAsync(
        string structureId,
        string recordId,
        DateOnly? asAt = null,
        CancellationToken cancellationToken = default);

    /// <summary>Whether <paramref name="recordId"/> sits at or below <paramref name="ancestorId"/>.</summary>
    Task<bool> IsUnderAsync(
        string structureId,
        string recordId,
        string ancestorId,
        DateOnly? asAt = null,
        CancellationToken cancellationToken = default);

    /// <summary>How far below its root the node sits, or null if it is not on this axis then.</summary>
    Task<int?> GetDepthAsync(
        string structureId,
        string recordId,
        DateOnly? asAt = null,
        CancellationToken cancellationToken = default);

    // ---- maintenance ------------------------------------------------------------------

    /// <summary>
    /// Gives a record its self pair on every structure whose levels include its dimension type,
    /// so a newly created record is resolvable as a root of those axes immediately.
    /// </summary>
    Task EnsureSelfPairsAsync(
        string recordId,
        string dimensionTypeId,
        EffectiveRange effectiveRange,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reacts to a structure's levels changing: gives self pairs to the records of a dimension
    /// type the structure has just gained, and closes the rows for one it has lost.
    /// </summary>
    /// <remarks>
    /// Adding a level is retrospective by nature — the records of that type already exist, and
    /// they become part of this axis the moment the level is declared. Without this they would
    /// be invisible on an axis they belong to, with nothing to indicate why.
    /// </remarks>
    Task OnStructureLevelsChangedAsync(
        string structureId,
        IReadOnlyList<string> addedDimensionTypeIds,
        IReadOnlyList<string> removedDimensionTypeIds,
        DateOnly effectiveDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reparents a node from <paramref name="effectiveFrom"/>, closing the old link the day
    /// before and rebuilding the closure for the moved subtree only.
    /// </summary>
    /// <param name="newParentId">The new parent, or null to make the node a root of this axis.</param>
    Task<DimensionResult<int>> MoveAsync(
        string structureId,
        string recordId,
        string? newParentId,
        DateOnly effectiveFrom,
        CancellationToken cancellationToken = default);

    /// <summary>Removes a node's links and closure rows on every structure. Used by deletion.</summary>
    Task RemoveAsync(string recordId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reconstructs the whole closure index for a structure from the link table.
    /// </summary>
    /// <returns>How many closure documents were written.</returns>
    Task<int> RebuildAsync(string structureId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Compares the closure index against the links and reports any divergence, changing
    /// nothing.
    /// </summary>
    /// <param name="asAtDates">
    /// The dates to compare on. When null the service picks them: every date on which any link
    /// on this structure starts or ends, and the day either side of each. A check that only
    /// looked at today would miss exactly the drift that matters, because the index is correct
    /// today far more often than it is correct for last March.
    /// </param>
    Task<ClosureVerificationReport> VerifyAsync(
        string structureId,
        IReadOnlyList<DateOnly>? asAtDates = null,
        CancellationToken cancellationToken = default);
}
