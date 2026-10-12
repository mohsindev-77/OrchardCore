using WorkMate.Core;
using WorkMate.Records.Models;

namespace WorkMate.Records.Services;

/// <summary>
/// The one way in to the tenant's managed lookup lists.
/// </summary>
/// <remarks>
/// One aggregate, one service (CLAUDE.md rule 9). The admin screens, the <c>lookup-lists</c> recipe
/// step and the export all go through here, so there is no privileged path and no second place a
/// rule could be enforced differently.
///
/// <b>This interface is the seam ADR-0014 rests on.</b> Nothing outside this service knows a list
/// is a YesSql document rather than a taxonomy; if that decision is overturned, the implementation
/// changes and every caller written in a later slice does not.
///
/// <b>What this service will not do, and why.</b> It never deletes an entry and never changes one's
/// code. An entry is the value a record holds, so taking it away leaves records naming something
/// nothing can resolve — and nothing reports that, because a report grouping by a missing code
/// simply comes out short. <see cref="UpdateAsync"/> refuses both, naming the entry and saying to
/// retire it instead. That is the same rule <c>DimensionTypeService.UpdateAsync</c> applies to an
/// attribute schema, reached independently and for the same reason.
/// </remarks>
public interface ILookupListService
{
    /// <summary>The list with this id, or null.</summary>
    Task<LookupListDocument?> GetAsync(string lookupListId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The list with this code, or null. Case-insensitive, per <see cref="LookupCodes.Comparer"/>.
    /// </summary>
    /// <remarks>
    /// The read a recipe step, an export and — from slice B2 — a bound field's editor all use,
    /// because the code is the portable name and the id is not.
    /// </remarks>
    Task<LookupListDocument?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every list in the tenant, ordered by code.
    /// </summary>
    /// <remarks>
    /// Unpaged, unlike <c>IEmployeeService.ListAsync</c>, and that asymmetry is deliberate: a tenant
    /// holds tens of thousands of employees and tens of lookup lists. A page control over a dozen
    /// rows is a control nobody uses and a shape every caller then has to loop around.
    /// </remarks>
    Task<IReadOnlyList<LookupListDocument>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Creates a list with its entries.</summary>
    /// <param name="isSystemDefined">
    /// True for the lists WorkMate ships through a recipe, which a customer may extend and may not
    /// delete.
    /// </param>
    Task<RecordResult<LookupListDocument>> CreateAsync(
        string code,
        BilingualText name,
        IReadOnlyList<LookupListItem> items,
        bool isSystemDefined = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// States the list's name and the whole of its entries.
    /// </summary>
    /// <remarks>
    /// The whole set, not a delta, because "exactly one entry is at sort order 20" and "these codes
    /// are the ones this list offers" are statements about the set — the same reason the placement
    /// screen states a whole split rather than one row of it. What the caller may not do is state a
    /// set that drops an entry the tenant already holds, or that changes one's code; both are
    /// refused with <see cref="RecordRule.ImmutableOnceInUse"/>.
    /// </remarks>
    Task<RecordResult<LookupListDocument>> UpdateAsync(
        string lookupListId,
        BilingualText name,
        IReadOnlyList<LookupListItem> items,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a list that holds no entries.
    /// </summary>
    /// <remarks>
    /// The one destructive operation here, and it is narrow on purpose: with no entries there is
    /// provably nothing for a record to be holding, so a mistyped code is recoverable without
    /// opening a hole. A list with entries is refused with
    /// <see cref="RecordRule.LookupListNotEmpty"/>, and so is a system-defined one.
    /// </remarks>
    Task<RecordResult<LookupListDocument>> DeleteAsync(
        string lookupListId,
        CancellationToken cancellationToken = default);
}
