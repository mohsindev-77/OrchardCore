using OrchardCore.ContentManagement;

namespace WorkMate.Dimensions.Models;

/// <summary>
/// The standard part every generated dimension content type carries: the fields that are the
/// same whatever kind of unit this is. A type's attribute schema adds the rest, on a separate
/// part named for the type.
/// </summary>
/// <remarks>
/// Public setters, unlike the rest of this module's domain models. A content part is the one
/// place the coding standard allows them, and Orchard requires them: it materialises a part by
/// deserialising the content item's JSON into it, and the display driver writes to it when an
/// editor posts.
///
/// Not sealed, for the same reason <see cref="WorkMate.Platform.Fields.BilingualTextField"/> is
/// not: Orchard builds parts and their editor shapes through proxies.
///
/// Note what is <em>not</em> here. There is no parent field and no structure field. A record's
/// placement is a link row scoped to a structure, because one record sits on several axes at
/// once and a single parent field collapses them into one tree — decision 4 of the dimension
/// engine architecture. Adding a parent field here would quietly undo that.
/// </remarks>
public class DimensionRecordPart : ContentPart
{
    /// <summary>
    /// The customer-facing code, unique within the tenant including against retired records.
    /// This is what recipes and imports resolve a record by, so it is the one field an external
    /// system is allowed to know.
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>The English name. The sort key, and what the generated title is driven from.</summary>
    public string NameEn { get; set; } = string.Empty;

    /// <summary>The Arabic name.</summary>
    public string NameAr { get; set; } = string.Empty;

    /// <summary>
    /// A short label a customer recognises the unit by, in English. Optional.
    /// </summary>
    /// <remarks>
    /// FRS v2.5 §4.1: every dimension's list and tree carry Code, Name, Alias and Status. The
    /// examples in its figures are what an alias is for — <em>Head Office</em> against KKT Cayman,
    /// <em>Hiring Team</em> against Recruitment, <em>Band E1 — reports to Board</em> against Chief
    /// Executive Officer. It is how people in the business talk about the unit, next to what it is
    /// formally called.
    ///
    /// <b>Not a name, and deliberately not dated.</b> The bilingual name carries a dated history
    /// (<c>DimensionNameDocument</c>) because "what was this called last March" changes what a
    /// historical report prints, and because a rename is two different operations — substantive or
    /// corrective — that the engine has to be told apart. None of that is true of an alias: nothing
    /// resolves it as at a date, no report groups by it, and "was this nickname different last
    /// March" is a question nobody asks. Dating it would mean a second parallel history, with its
    /// own storage and its own two kinds of rename, to answer a question that does not exist.
    ///
    /// So it sits here with <see cref="CostCentreCode"/> and <see cref="GlAccountRef"/>: a plain
    /// field on the record, changed by <c>IDimensionService.SetAliasAsync</c>, which takes no
    /// effective date because there is nothing to date.
    ///
    /// <b>Bilingual, where the FRS mock shows one box.</b> CLAUDE.md rule 7 — every name field is
    /// bilingual — and an alias is a name whatever else it is. The Arabic half is optional like
    /// every other Arabic half since ADR-0003's addendum, so a tenant that never fills it in sees
    /// exactly the single box the mock draws.
    /// </remarks>
    public string AliasEn { get; set; } = string.Empty;

    /// <inheritdoc cref="AliasEn"/>
    public string AliasAr { get; set; } = string.Empty;

    /// <summary>Which dimension type this is a record of. Set on create and never changed.</summary>
    public string DimensionTypeId { get; set; } = string.Empty;

    /// <summary>The first day this record is effective.</summary>
    public DateOnly EffectiveFrom { get; set; }

    /// <summary>
    /// The last day this record is effective, inclusive, or null while it is open ended. A
    /// retired record carries a date here and keeps resolving for historical queries.
    /// </summary>
    public DateOnly? EffectiveTo { get; set; }

    /// <summary>
    /// Whether the record is offered in pickers. Distinct from the effective range on purpose: a
    /// unit can be temporarily withdrawn from use without its history being rewritten.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Where this record sits among its siblings. Equal values fall back to the English name.</summary>
    public int SortOrder { get; set; }

    /// <summary>The cost centre this unit books to, where the customer tracks that on the unit.</summary>
    public string CostCentreCode { get; set; } = string.Empty;

    /// <summary>The general ledger account reference, for the finance export.</summary>
    public string GlAccountRef { get; set; } = string.Empty;

    /// <summary>
    /// The employee who heads this unit, or empty when the post is vacant.
    /// </summary>
    /// <remarks>
    /// The head is not required to be assigned to the unit they head: a general manager heads
    /// three departments and belongs to none of them. The seed data in architecture section 8
    /// includes that case deliberately.
    /// </remarks>
    public string HeadEmployeeId { get; set; } = string.Empty;

    /// <summary>The effective range as the value object the services pass around.</summary>
    public WorkMate.Core.EffectiveRange EffectiveRange => new(EffectiveFrom, EffectiveTo);

    /// <summary>Whether the record is effective on <paramref name="asAt"/>.</summary>
    public bool IsEffectiveOn(DateOnly asAt) => EffectiveRange.Contains(asAt);
}
