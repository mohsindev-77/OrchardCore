using Microsoft.Extensions.Localization;
using OrchardCore.ContentManagement;
using OrchardCore.ContentManagement.Handlers;
using WorkMate.Platform.Services;
using WorkMate.Records.Indexes;
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
    private readonly IStringLocalizer S;

    public EmployeeHandler(
        ISession session,
        IBilingualNamePolicy namePolicy,
        IStringLocalizer<EmployeeHandler> stringLocalizer)
    {
        _session = session;
        _namePolicy = namePolicy;
        S = stringLocalizer;
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
