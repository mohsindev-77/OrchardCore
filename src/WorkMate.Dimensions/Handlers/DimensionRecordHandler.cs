using OrchardCore.ContentManagement;
using OrchardCore.ContentManagement.Handlers;
using WorkMate.Dimensions.Models;

namespace WorkMate.Dimensions.Handlers;

/// <summary>
/// Runs the dimension record invariants at the content <em>item</em> level.
/// </summary>
/// <remarks>
/// This exists because of a trap in Orchard Core 3.0.1 that cost real time to find, and it is
/// worth writing down rather than rediscovering.
///
/// The natural place for these checks is
/// <c>ContentPartHandler&lt;DimensionRecordPart&gt;.ValidatingAsync</c>. That hook takes a
/// <c>ValidateContentPartContext</c>, which is constructed with its own
/// <c>ContentValidateResult</c> — so <c>context.Fail(...)</c> there records the failure on an
/// object the caller never sees. <c>IContentManager.ValidateAsync</c> returns success, the
/// record saves, and nothing anywhere reports a problem. A handler written that way compiles,
/// runs, and silently validates nothing, which is the worst of the three.
///
/// An <c>IContentHandler</c> gets the real <c>ValidateContentContext</c> — the one whose result
/// is handed back — so failures recorded here actually reject the save. The rules themselves
/// stay on <see cref="DimensionRecordPartHandler"/>, next to the part they are about; this type
/// only provides the context that works.
/// </remarks>
public sealed class DimensionRecordHandler : ContentHandlerBase
{
    private readonly DimensionRecordPartHandler _rules;

    public DimensionRecordHandler(DimensionRecordPartHandler rules) => _rules = rules;

    public override async Task ValidatingAsync(ValidateContentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Every content item on the tenant passes through here, so the first thing to establish
        // is whether this one is any of our business.
        if (!context.ContentItem.TryGet<DimensionRecordPart>(out var part))
        {
            return;
        }

        await _rules.ValidateAsync(context, part);
    }
}
