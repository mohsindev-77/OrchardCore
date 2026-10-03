using OrchardCore.ContentManagement;
using OrchardCore.ContentManagement.Handlers;
using OrchardCore.ContentManagement.Metadata;
using WorkMate.Dimensions.Models;
using WorkMate.Dimensions.Services;

namespace WorkMate.Dimensions.Handlers;

/// <summary>
/// Makes a dimension record correct whatever created it, and whenever.
/// </summary>
/// <remarks>
/// This type exists because of two separate traps in Orchard Core 3.0.1, both of which produce
/// silently wrong data rather than an error. Both are worth writing down rather than
/// rediscovering.
///
/// <b>One: a part handler cannot reject a save.</b> The natural place for the invariants is
/// <c>ContentPartHandler&lt;DimensionRecordPart&gt;.ValidatingAsync</c>. That hook takes a
/// <c>ValidateContentPartContext</c>, which is constructed with its own
/// <c>ContentValidateResult</c> — so <c>context.Fail(...)</c> there records the failure on an
/// object the caller never sees. <c>IContentManager.ValidateAsync</c> returns success, the
/// record saves, and nothing reports a problem. An <c>IContentHandler</c> gets the real
/// <c>ValidateContentContext</c>, whose result is the one handed back.
///
/// <b>Two: a content type created in the current scope is invisible to
/// <c>IContentManager.NewAsync</c>.</b> See ADR-0006. <c>NewAsync</c> reads the <em>cached</em>
/// content definition, which does not refresh until the scope commits, so for a type created
/// moments earlier it reads nothing and returns an item with <b>no parts welded at all</b> —
/// not the standard part, not <c>TitlePart</c>, and not the part carrying the type's own
/// attribute fields. Nothing throws. The caller gets an item that looks usable and silently
/// drops every field it is given. <see cref="ActivatingAsync"/> repairs that.
///
/// The validation rules themselves stay on <see cref="DimensionRecordPartHandler"/>, next to
/// the part they are about; this type only provides the context and the item state that work.
/// </remarks>
public sealed class DimensionRecordHandler : ContentHandlerBase
{
    private readonly DimensionRecordPartHandler _rules;
    private readonly IDimensionTypeService _dimensionTypeService;
    private readonly IContentDefinitionManager _contentDefinitionManager;
    private readonly ITypeActivatorFactory<ContentPart> _partFactory;

    public DimensionRecordHandler(
        DimensionRecordPartHandler rules,
        IDimensionTypeService dimensionTypeService,
        IContentDefinitionManager contentDefinitionManager,
        ITypeActivatorFactory<ContentPart> partFactory)
    {
        _rules = rules;
        _dimensionTypeService = dimensionTypeService;
        _contentDefinitionManager = contentDefinitionManager;
        _partFactory = partFactory;
    }

    /// <summary>
    /// Welds the parts Orchard could not, when the content type was created in this same scope.
    /// </summary>
    /// <remarks>
    /// The fast path is a single cached read and costs nothing: when the cached definition is
    /// present — which it is for every content type that existed before this scope began, so
    /// virtually always — Orchard has already welded everything and this returns immediately.
    /// The repair only runs when the cached definition is missing, which happens exactly in the
    /// window ADR-0006 describes: a recipe or an import creating a dimension type and then its
    /// records in one unit of work, which is what prompt 3 does.
    ///
    /// Every part on the definition is welded, not just <c>DimensionRecordPart</c>. The part
    /// named for the content type is the one carrying the dimension type's own attribute
    /// schema, and a record created without it would accept values for its custom fields and
    /// store none of them.
    /// </remarks>
    public override async Task ActivatingAsync(ActivatingContentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (await _contentDefinitionManager.GetTypeDefinitionAsync(context.ContentType) is not null)
        {
            return;
        }

        // The cached definition is missing. Either this is one of our types created in this
        // scope, or it is not a content type at all; the index says which, and the index is
        // written in this scope so it can see it.
        if (await _dimensionTypeService.GetByContentTypeAsync(context.ContentType) is null)
        {
            return;
        }

        var definition = await _contentDefinitionManager.LoadTypeDefinitionAsync(context.ContentType);

        if (definition is null)
        {
            return;
        }

        foreach (var part in definition.Parts)
        {
            if (context.ContentItem.Has(part.Name))
            {
                continue;
            }

            var activator = _partFactory.GetTypeActivator(part.PartDefinition.Name);

            context.ContentItem.Weld(part.Name, activator.CreateInstance());
        }
    }

    public override async Task ValidatingAsync(ValidateContentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Every content item on the tenant passes through here, so the first thing to establish
        // is whether this one is any of our business.
        if (!context.ContentItem.TryGet<DimensionRecordPart>(out var part))
        {
            return;
        }

        // The part handler's InitializingAsync stamps the dimension type on a new record, but
        // it only runs if the part was welded when the item was activated — which, for a type
        // created in this scope, it was not. Stamping here as well means the rule holds on
        // every path into a record, including the one that bypasses part handlers entirely.
        if (string.IsNullOrEmpty(part.DimensionTypeId))
        {
            var type = await _dimensionTypeService.GetByContentTypeAsync(context.ContentItem.ContentType);

            if (type is not null)
            {
                part.DimensionTypeId = type.DimensionTypeId;
                context.ContentItem.Apply(nameof(DimensionRecordPart), part);
            }
        }

        await _rules.ValidateAsync(context, part);
    }
}
