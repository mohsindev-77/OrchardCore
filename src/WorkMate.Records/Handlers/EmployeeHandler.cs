using Microsoft.Extensions.Localization;
using OrchardCore.ContentManagement;
using OrchardCore.ContentManagement.Handlers;
using OrchardCore.ContentManagement.Metadata;
using WorkMate.Platform.Services;
using WorkMate.Records.Indexes;
using WorkMate.Records.Internal;
using WorkMate.Records.Models;
using WorkMate.Records.Services;
using YesSql;

namespace WorkMate.Records.Handlers;

/// <summary>
/// The chokepoint every employee record passes through, whatever created it.
/// </summary>
/// <remarks>
/// The <c>Employee</c> type is creatable, listable and securable, so the admin screens, the API,
/// GraphQL, a recipe and an import can all produce one. This is the only point all five share, and
/// the rules that must hold for every employee therefore live here rather than in
/// <c>IEmployeeService</c> alone.
///
/// <b>An <c>IContentHandler</c>, not a <c>ContentPartHandler</c>, and that is not a style choice.</b>
/// <c>ContentPartHandler&lt;T&gt;.ValidatingAsync</c> receives a <c>ValidateContentPartContext</c>
/// constructed with its own <c>ContentValidateResult</c>, so <c>context.Fail(...)</c> there records
/// the failure on an object the caller never sees: <c>IContentManager.ValidateAsync</c> returns
/// success and the record saves. <c>DimensionRecordHandler</c> carries the full account of that
/// trap; it applies here unchanged.
///
/// The rules are deliberately the invariants rather than the whole rule set. Anything that needs to
/// know what the record said <em>before</em> the edit — that the code has not changed, that a
/// lifecycle move is permitted — belongs on <see cref="IEmployeeService"/>, which has both states.
/// A handler sees only what is being saved.
/// </remarks>
public sealed class EmployeeHandler : ContentHandlerBase
{
    private readonly ISession _session;
    private readonly IBilingualNamePolicy _namePolicy;

    /// <summary>For the ADR-0006 repair. See <see cref="ActivatingAsync"/>.</summary>
    private readonly IContentDefinitionManager _contentDefinitionManager;

    /// <summary>Builds a part instance to weld when Orchard did not. ADR-0006.</summary>
    private readonly ITypeActivatorFactory<ContentPart> _partFactory;

    /// <summary>Whether this scope altered the Employee definition. ADR-0006, slice B3.</summary>
    private readonly ContentDefinitionChangesInThisScope _changes;

    private readonly IStringLocalizer S;

    public EmployeeHandler(
        ISession session,
        IBilingualNamePolicy namePolicy,
        IContentDefinitionManager contentDefinitionManager,
        ITypeActivatorFactory<ContentPart> partFactory,
        ContentDefinitionChangesInThisScope changes,
        IStringLocalizer<EmployeeHandler> stringLocalizer)
    {
        _session = session;
        _namePolicy = namePolicy;
        _contentDefinitionManager = contentDefinitionManager;
        _partFactory = partFactory;
        _changes = changes;
        S = stringLocalizer;
    }

    /// <summary>
    /// Welds the parts Orchard could not see, when a definition changed in this same scope.
    /// ADR-0006; slice B3.
    /// </summary>
    /// <remarks>
    /// <b>The failure this repairs, measured rather than feared.</b> ADR-0006: inside the scope
    /// that altered a content definition, <c>IContentDefinitionManager</c>'s <em>cached</em> read
    /// cannot see the change, and <c>IContentManager.NewAsync</c> is one of its callers. Given a
    /// type it cannot see properly, <c>NewAsync</c> returns an item with <b>no parts welded at
    /// all</b> — not <c>EmployeePart</c>, not <c>TitlePart</c>, not the bags. Nothing throws. The
    /// caller gets an item that looks usable, silently discards every field set on it, and then
    /// fails validation about something else entirely.
    ///
    /// <b>Why the employee record needs this NOW and did not before.</b> Until B3 nothing altered
    /// <c>Employee</c>'s definition at run time — the six sections arrive in a migration, long
    /// before anybody creates an employee. <c>IFormDefinitionService.PublishAsync</c> changes
    /// that: a recipe can publish a form and then create employees in the same shell scope, which
    /// is precisely the sequence the ADR describes and precisely the one B6's recipe step will
    /// perform. <c>DimensionRecordHandler.ActivatingAsync</c> has carried this repair since
    /// prompt 3; this is the same code for the same reason, and the ADR named it as slice B3's to
    /// write.
    ///
    /// <b>It costs one cache read on the ordinary path.</b> The cached definition is present for
    /// every type that existed before the scope began — virtually always — and the method returns
    /// immediately. Only when it is missing does anything else happen.
    ///
    /// <b>Scoped to this module's own type</b>, because a handler is called for every content item
    /// in the tenant and welding parts onto somebody else's type on the strength of a cache miss
    /// would be this module repairing a problem that is not its to diagnose.
    /// </remarks>
    public override async Task ActivatingAsync(ActivatingContentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!string.Equals(context.ContentType, EmployeeFieldNames.ContentType, StringComparison.Ordinal))
        {
            return;
        }

        // TWO WAYS THE CACHE IS WRONG, and the cheap one is checked first.
        //
        // A type CREATED in this scope: the cached read returns null, which is ADR-0006's own
        // case and the one DimensionRecordHandler was written for.
        //
        // A type ALTERED in this scope — which is what a form publish does to Employee — fails
        // more quietly: the cached read returns the definition as it was, so NewAsync welds the
        // OLD parts and silently drops everything written into the new section. Nothing is null,
        // so no null check can see it. The scope that made the change says so instead; see
        // ContentDefinitionChangesInThisScope for why a marker beats comparing definitions.
        if (!_changes.WasAltered(context.ContentType) &&
            await _contentDefinitionManager.GetTypeDefinitionAsync(context.ContentType) is not null)
        {
            return;
        }

        // The cached definition is blind to a change made in this scope. The loaded one is not —
        // that is the whole of ADR-0006's rule — so it is the authority for what should be welded.
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

            // EVERY part on the definition, not only this module's own. The bags carry the
            // sections, and a designer-added section is exactly the part a publish in this scope
            // has just created — so welding only EmployeePart would repair the half of the failure
            // that was not the reason this was written.
            var activator = _partFactory.GetTypeActivator(part.PartDefinition.Name);

            context.ContentItem.Weld(part.Name, activator.CreateInstance());
        }
    }

    /// <summary>
    /// Gives the employee the display text every Orchard screen shows them by: their English name.
    /// </summary>
    /// <remarks>
    /// The <c>Employee</c> type binds <c>TitlePart</c> to the English name with a generated
    /// pattern, and on a real tenant that produces nothing, for a reason that has nothing to do
    /// with this module: <c>TitlePartHandler</c> renders the pattern through
    /// <c>ILiquidTemplateManager</c>, which throws outside an HTTP request. Every dimension record
    /// has the same empty title for the same reason;
    /// <c>DimensionRecordHandler.SetDisplayText</c> carries the full account.
    ///
    /// The handler is therefore the authority. It and the pattern cannot disagree — both are "the
    /// English name" — and the pattern is kept so a path where Liquid does work gives the same
    /// answer rather than a different one.
    /// </remarks>
    private static void SetDisplayText(ContentItem contentItem)
    {
        if (contentItem.TryGet<EmployeePart>(out var part) && !string.IsNullOrWhiteSpace(part.NameEn))
        {
            contentItem.DisplayText = part.NameEn;
        }
    }

    public override Task CreatingAsync(CreateContentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        SetDisplayText(context.ContentItem);

        return Task.CompletedTask;
    }

    public override Task UpdatingAsync(UpdateContentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        SetDisplayText(context.ContentItem);

        return Task.CompletedTask;
    }

    public override Task PublishingAsync(PublishContentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        SetDisplayText(context.ContentItem);

        return Task.CompletedTask;
    }

    public override async Task ValidatingAsync(ValidateContentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Every content item on the tenant passes through here, so the first thing to establish is
        // whether this one is any of our business.
        if (!context.ContentItem.TryGet<EmployeePart>(out var part))
        {
            return;
        }

        var code = EmployeeCodes.Normalise(part.EmployeeCode);

        if (code.Length == 0)
        {
            context.Fail(
                S["An employee needs a code. It is the natural key every recipe, export and integration names them by."],
                nameof(EmployeePart.EmployeeCode));
        }
        else if (!EmployeeCodes.IsValid(code))
        {
            context.Fail(
                S["'{0}' is not a valid employee code. Use up to {1} letters, digits, hyphens, underscores, dots or slashes, with no spaces.",
                    code,
                    EmployeeCodes.MaxLength],
                nameof(EmployeePart.EmployeeCode));
        }
        else
        {
            await ValidateCodeIsUnusedAsync(context, code);
        }

        if (string.IsNullOrWhiteSpace(part.NameEn))
        {
            context.Fail(S["An employee needs an English name."], nameof(EmployeePart.NameEn));
        }

        // ADR-0003's addendum: Arabic is optional unless this tenant has turned it on. Asked
        // through the same one-question seam every other write path on the platform uses, so the
        // employee record and the organisation chart cannot disagree about the answer.
        if (string.IsNullOrWhiteSpace(part.NameAr) && await _namePolicy.RequiresArabicAsync())
        {
            context.Fail(S["This tenant requires an Arabic name."], nameof(EmployeePart.NameAr));
        }

        if (part.JoinDate == default)
        {
            context.Fail(
                S["An employee needs a join date. Nothing is defaulted on a write."],
                nameof(EmployeePart.JoinDate));
        }

        if (part.StatusEffectiveFrom == default)
        {
            context.Fail(
                S["An employee's status needs a date it took effect from."],
                nameof(EmployeePart.StatusEffectiveFrom));
        }

        if (part.DateOfBirth is not null && part.JoinDate != default && part.DateOfBirth >= part.JoinDate)
        {
            context.Fail(
                S["A date of birth of {0} is not before the join date of {1}.", part.DateOfBirth.Value, part.JoinDate],
                nameof(EmployeePart.DateOfBirth));
        }
    }

    /// <summary>
    /// That no other employee already holds this code — including one who has left.
    /// </summary>
    /// <remarks>
    /// Compared on <c>CodeUpper</c> rather than with a case-insensitive query, because collation is
    /// the database's property and not ours: SQLite's default is case-sensitive and SQL Server's
    /// usual one is not, so the same query would admit <c>EMP-1</c> alongside <c>emp-1</c> on one
    /// customer and refuse it on another.
    ///
    /// Filtered to the latest version and excluding this item, or saving an employee a second time
    /// would report them as a duplicate of their own earlier version.
    ///
    /// This closes the ordinary case and not the simultaneous one: two requests creating the same
    /// code at the same instant can both pass and both commit, because validation is a read
    /// followed by a write. That is the same known limitation the dimension engine records for
    /// record codes, with the same cause — YesSql 5.4.7's schema builder offers no unique index —
    /// and the same consequence, which is a duplicate code rather than lost data.
    /// </remarks>
    private async Task ValidateCodeIsUnusedAsync(ValidateContentContext context, string code)
    {
        var upper = code.ToUpperInvariant();
        var itemId = context.ContentItem.ContentItemId;

        var clash = await _session
            .QueryIndex<EmployeeIndex>(index =>
                index.CodeUpper == upper &&
                index.ContentItemId != itemId &&
                index.Latest)
            .FirstOrDefaultAsync();

        if (clash is not null)
        {
            context.Fail(
                S["The employee code '{0}' is already used by {1}.", clash.Code, clash.NameEn],
                nameof(EmployeePart.EmployeeCode));
        }
    }
}
