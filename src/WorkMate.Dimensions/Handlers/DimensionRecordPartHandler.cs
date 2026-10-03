using Microsoft.Extensions.Localization;
using OrchardCore.ContentManagement.Handlers;

using WorkMate.Dimensions.Models;
using WorkMate.Dimensions.Services;


namespace WorkMate.Dimensions.Handlers;

/// <summary>
/// The one place every dimension record passes through, whatever created it.
/// </summary>
/// <remarks>
/// Generated dimension types are creatable, as specification section 4 requires, so an
/// administrator can create one from Orchard's standard content screens with no placement and
/// no validation from the designer. The API, GraphQL, a recipe and a bulk import can all do the
/// same. Blocking the admin screen would close one of five doors.
///
/// So the invariants are enforced here instead, because a content handler is the only point all
/// five paths share. A record that breaks them is rejected through Orchard's own content
/// validation, which the standard editor already renders.
///
/// A record created this way is <em>unplaced</em>: it exists with no parent on any structure.
/// That is a legitimate state, not an error — a new top-level unit genuinely has no parent, and
/// architecture section 6 already warns rather than blocks on pre-built future structures. It
/// carries no flag, because unplaced is derivable: a record with no link on a structure whose
/// levels include its type, whose type is not at ordinal zero. The organisation designer lists
/// those. Placement itself stays an explicit operation needing <c>MoveDimensionRecords</c>, so
/// creating a record from the generic UI cannot place it, which is the actual risk.
///
/// Business logic lives in services: this handler asks, it does not decide. The closure and link
/// work it must trigger belongs to <c>IDimensionGraphService</c> and lands with the graph layer,
/// in the same ambient session so that a record and its index rows commit together.
///
/// <b>Code uniqueness is not checked here, and that is a known gap.</b> It cannot be done
/// reliably from a validation handler: by the time validation runs, the content manager has
/// already saved the item under validation, so its own row is in the index and telling it apart
/// from a genuine clash proved unreliable in practice. Worse, a record saved earlier in the same
/// unit of work is not in the index at all, so an import creating five hundred rows in one batch
/// would miss duplicates within the batch — which is exactly where they happen.
///
/// It belongs to <c>IDimensionValidator</c>, which lands next and has to keep track of the codes
/// seen within a batch regardless: architecture section 6 requires an import to validate row by
/// row and report every failure, which is the same bookkeeping. Until then the rule is enforced
/// on dimension types and structures, where it works, but not on records. The module README
/// records it.
/// </remarks>
public sealed class DimensionRecordPartHandler : ContentPartHandler<DimensionRecordPart>
{
    private readonly IDimensionTypeService _dimensionTypeService;
    private readonly IStringLocalizer S;

    public DimensionRecordPartHandler(
        IDimensionTypeService dimensionTypeService,
        IStringLocalizer<DimensionRecordPartHandler> stringLocalizer)
    {
        _dimensionTypeService = dimensionTypeService;
        S = stringLocalizer;
    }

    /// <summary>
    /// Stamps the dimension type onto a new record from the content type it is being created in.
    /// </summary>
    /// <remarks>
    /// The content type <em>is</em> the dimension type, so asking the user to pick one would be
    /// asking them to repeat themselves and giving them a way to disagree with the answer.
    /// </remarks>
    public override async Task InitializingAsync(InitializingContentContext context, DimensionRecordPart part)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(part);

        if (!string.IsNullOrEmpty(part.DimensionTypeId))
        {
            return;
        }

        var type = await _dimensionTypeService.GetByContentTypeAsync(context.ContentItem.ContentType);

        if (type is not null)
        {
            part.DimensionTypeId = type.DimensionTypeId;
        }
    }

    /// <summary>
    /// The invariants every dimension record has to satisfy, whatever created it.
    /// </summary>
    internal async Task ValidateAsync(ValidateContentContext context, DimensionRecordPart part)
    {
        if (string.IsNullOrWhiteSpace(part.Code))
        {
            context.Fail(S["A code is required."], nameof(part.Code));
        }
        else if (!DimensionCodes.IsValidCode(part.Code))
        {
            context.Fail(
                S["A code must start with a letter and may contain letters, digits, hyphens and underscores, up to fifty characters."],
                nameof(part.Code));
        }

        if (string.IsNullOrWhiteSpace(part.NameEn) || string.IsNullOrWhiteSpace(part.NameAr))
        {
            context.Fail(S["A name is required in both English and Arabic."], nameof(part.NameEn));
        }

        // No defaulting on a write, per specification section 2 rule 3. A record saved with no
        // effective date would resolve from 0001-01-01, which is silently wrong rather than
        // loudly wrong.
        if (part.EffectiveFrom == default)
        {
            context.Fail(S["An effective from date is required."], nameof(part.EffectiveFrom));
        }
        else if (part.EffectiveTo is { } to && to < part.EffectiveFrom)
        {
            context.Fail(
                S["The effective to date cannot be before the effective from date."],
                nameof(part.EffectiveTo));
        }

        if (string.IsNullOrEmpty(part.DimensionTypeId))
        {
            // Only reachable if the content type is not one this module generated, which means
            // the part has been attached to something by hand.
            context.Fail(
                S["This content type is not a dimension type, so it cannot carry a dimension record."],
                nameof(part.DimensionTypeId));
        }
    }
}
