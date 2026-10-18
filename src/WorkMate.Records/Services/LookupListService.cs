using Microsoft.Extensions.Localization;
using OrchardCore.AuditTrail.Services;
using OrchardCore.AuditTrail.Services.Models;
using IIdGenerator = OrchardCore.Entities.IIdGenerator;
using OrchardCore.Environment.Cache;
using OrchardCore.Environment.Shell;
using WorkMate.Core;
using WorkMate.Platform.Services;
using WorkMate.Records.Indexes;
using WorkMate.Records.Internal;
using WorkMate.Records.Models;
using YesSql;

namespace WorkMate.Records.Services;

/// <inheritdoc />
internal sealed class LookupListService : ILookupListService
{
    private readonly ISession _session;
    private readonly IRecordsAuthorisation _authorisation;
    private readonly IEntitlementService _entitlement;
    private readonly IBilingualNamePolicy _namePolicy;
    private readonly IAuditTrailManager _auditTrailManager;
    private readonly IIdGenerator _idGenerator;

    /// <summary>
    /// Invalidates the cached lookup list reads after every write. See
    /// <see cref="CachedLookupListLookup"/> for the mechanism and <see cref="RecordsCacheKeys"/>
    /// for why one signal covers every list in the tenant rather than one signal per list.
    /// </summary>
    private readonly ISignal _signal;

    private readonly ShellSettings _shellSettings;
    private readonly IStringLocalizer S;

    public LookupListService(
        ISession session,
        IRecordsAuthorisation authorisation,
        IEntitlementService entitlement,
        IBilingualNamePolicy namePolicy,
        IAuditTrailManager auditTrailManager,
        IIdGenerator idGenerator,
        ISignal signal,
        ShellSettings shellSettings,
        IStringLocalizer<LookupListService> stringLocalizer)
    {
        _session = session;
        _authorisation = authorisation;
        _entitlement = entitlement;
        _namePolicy = namePolicy;
        _auditTrailManager = auditTrailManager;
        _idGenerator = idGenerator;
        _signal = signal;
        _shellSettings = shellSettings;
        S = stringLocalizer;
    }

    /// <remarks>
    /// <b>Through <see cref="CacheSignals.SignalNowAndAfterCommitAsync"/>, which is ADR-0019, rather
    /// than through <c>ISignal.SignalTokenAsync</c> alone.</b> This method used to call that
    /// directly, and claimed in this comment that doing so signalled "after the write". It did not.
    /// A write is staged on the session and committed when the shell scope is disposed, so an
    /// immediate signal lands before the commit, and a read arriving in between refills the cache
    /// from the state the write is about to replace, under a token nothing will signal again. On a
    /// tenant that had no lists, the ten writes of the standard-lists recipe left every bound field
    /// rendering a free-text box until the application was restarted. That is the defect this is
    /// written from; <c>LookupListInvalidationTenantTests</c> reproduces it.
    /// </remarks>
    private Task InvalidateCacheAsync() =>
        _signal.SignalNowAndAfterCommitAsync(RecordsCacheKeys.LookupListsSignal(_shellSettings.Name));

    /// <inheritdoc />
    public async Task<LookupListDocument?> GetAsync(
        string lookupListId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(lookupListId))
        {
            return null;
        }

        return await _session
            .Query<LookupListDocument, LookupListIndex>(index => index.LookupListId == lookupListId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<LookupListDocument?> GetByCodeAsync(
        string code,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var upper = LookupCodes.Normalise(code).ToUpperInvariant();

        if (upper.Length == 0)
        {
            return null;
        }

        // On the folded column, because that is what the comparison actually uses — see
        // LookupListIndex.CodeUpper for why the database's own collation is not trusted with it.
        return await _session
            .Query<LookupListDocument, LookupListIndex>(index => index.CodeUpper == upper)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LookupListDocument>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var lists = await _session.Query<LookupListDocument, LookupListIndex>().ListAsync(cancellationToken);

        // Ordered here rather than in the query because the comparison has to be ordinal and
        // case-insensitive — the same rule LookupCodes.Comparer states — and the database's
        // collation is not ours to assume.
        return [.. lists.OrderBy(list => list.Code, LookupCodes.Comparer)];
    }

    /// <inheritdoc />
    public async Task<RecordResult<LookupListDocument>> CreateAsync(
        string code,
        BilingualText name,
        IReadOnlyList<LookupListItem> items,
        bool isSystemDefined = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(items);
        cancellationToken.ThrowIfCancellationRequested();

        if (!await MayConfigureAsync())
        {
            return RecordResult.NotAuthorised<LookupListDocument>();
        }

        var normalised = LookupCodes.Normalise(code);
        var errors = new List<RecordError>();

        if (!LookupCodes.IsValidListCode(normalised))
        {
            errors.Add(new RecordError(
                RecordRule.CodeFormat,
                normalised,
                S["'{0}' is not a valid list code. Use a letter followed by letters, digits, hyphens or underscores, up to {1} characters.",
                    normalised,
                    LookupCodes.MaxLength],
                Field: "Code"));
        }
        else if (await GetByCodeAsync(normalised, cancellationToken) is not null)
        {
            errors.Add(new RecordError(
                RecordRule.CodeUniqueness,
                normalised,
                S["There is already a lookup list with the code '{0}' in this tenant.", normalised],
                Field: "Code"));
        }

        errors.AddRange(await ValidateNameAsync(name, normalised, S["A lookup list needs an English name."], cancellationToken));
        errors.AddRange(await ValidateItemsAsync(items, existing: null, cancellationToken));

        if (errors.Count > 0)
        {
            return RecordResult.Failed<LookupListDocument>(errors);
        }

        var document = new LookupListDocument
        {
            LookupListId = _idGenerator.GenerateUniqueId(),
            Code = normalised,
            Name = Trimmed(name),
            IsSystemDefined = isSystemDefined,

            // D22, slice F6c: born retired. The standard lists arrive through the lookup-lists
            // recipe step, which runs AFTER this module's migrations — so a step that retired them
            // could only ever catch a tenant that already had them, and every new tenant would
            // load job-title and grade fresh and unretired. Said here instead, where every path
            // that creates a list goes through: the recipe, the standard-lists action and any
            // later import.
            //
            // They are still created, and that is the point: a tenant upgrading into F6 holds
            // records naming these codes, and a list that did not exist would leave them showing a
            // bare code for ever.
            IsRetired = StandardLookupLists.Superseded.Contains(normalised, StringComparer.Ordinal),
            Items = Ordered(items),
        };

        await _session.SaveCheckedAsync(document, cancellationToken);

        await RecordAsync(document, before: null, after: LookupListState.Of(document), operation: "Created");
        await InvalidateCacheAsync();

        return RecordResult.Success(document);
    }

    /// <inheritdoc />
    public async Task<RecordResult<LookupListDocument>> UpdateAsync(
        string lookupListId,
        BilingualText name,
        IReadOnlyList<LookupListItem> items,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(items);
        cancellationToken.ThrowIfCancellationRequested();

        if (!await MayConfigureAsync())
        {
            return RecordResult.NotAuthorised<LookupListDocument>();
        }

        var document = await GetAsync(lookupListId, cancellationToken);

        if (document is null)
        {
            return RecordResult.Failed<LookupListDocument>(UnknownList(lookupListId));
        }

        var before = LookupListState.Of(document);
        var errors = new List<RecordError>();

        errors.AddRange(await ValidateNameAsync(name, document.Code, S["A lookup list needs an English name."], cancellationToken));
        errors.AddRange(await ValidateItemsAsync(items, document.Items, cancellationToken));

        if (errors.Count > 0)
        {
            return RecordResult.Failed<LookupListDocument>(errors);
        }

        document.Name = Trimmed(name);
        document.Items = Ordered(items);

        await _session.SaveCheckedAsync(document, cancellationToken);

        await RecordAsync(document, before, LookupListState.Of(document), operation: "Updated");
        await InvalidateCacheAsync();

        return RecordResult.Success(document);
    }

    /// <inheritdoc />
    public async Task<RecordResult<LookupListDocument>> DeleteAsync(
        string lookupListId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!await MayConfigureAsync())
        {
            return RecordResult.NotAuthorised<LookupListDocument>();
        }

        var document = await GetAsync(lookupListId, cancellationToken);

        if (document is null)
        {
            return RecordResult.Failed<LookupListDocument>(UnknownList(lookupListId));
        }

        if (document.IsSystemDefined)
        {
            return RecordResult.Failed<LookupListDocument>(new RecordError(
                RecordRule.LookupListNotEmpty,
                document.Code,
                S["'{0}' is defined by WorkMate and cannot be deleted. Its entries can still be retired.", document.Code]));
        }

        if (document.Items.Count > 0)
        {
            return RecordResult.Failed<LookupListDocument>(new RecordError(
                RecordRule.LookupListNotEmpty,
                document.Code,
                S["'{0}' still holds entries and cannot be deleted, because a record may already hold one of them. Retire the entries you no longer offer; a list can only be deleted while it is empty.",
                    document.Code]));
        }

        var before = LookupListState.Of(document);

        _session.Delete(document);

        await RecordAsync(document, before, after: null, operation: "Deleted");
        await InvalidateCacheAsync();

        return RecordResult.Success(document);
    }

    /// <inheritdoc />
    public async Task<RecordResult<LookupListDocument>> RetireAsync(
        string lookupListId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!await MayConfigureAsync())
        {
            return RecordResult.NotAuthorised<LookupListDocument>();
        }

        var document = await GetAsync(lookupListId, cancellationToken);

        if (document is null)
        {
            return RecordResult.Failed<LookupListDocument>(UnknownList(lookupListId));
        }

        if (document.IsRetired)
        {
            // Already said. A second retirement is not an error and must not file a second audit
            // entry about a change nobody made — the same no-op guard SetLineManagerAsync keeps.
            return RecordResult.Success(document);
        }

        var before = LookupListState.Of(document);

        document.IsRetired = true;

        await _session.SaveCheckedAsync(document, cancellationToken);
        await RecordAsync(document, before, LookupListState.Of(document), operation: "Retired");
        await InvalidateCacheAsync();

        return RecordResult.Success(document);
    }

    /// <summary>
    /// The permission and the entitlement, in that order, for every write.
    /// </summary>
    /// <remarks>
    /// CLAUDE.md rule 5: both are declared in the module and checked in the service, not only in a
    /// controller, so the recipe step and any future API are covered by the same check the screen
    /// is. The entitlement answer is yes to everything until prompt 8 builds the entitlement
    /// record — see <see cref="IEntitlementService"/> and <see cref="Capabilities"/> for why the
    /// question is asked anyway.
    ///
    /// Permission first. A caller who may not do this at all should be refused for that reason
    /// rather than told which edition would have let them.
    /// </remarks>
    private async Task<bool> MayConfigureAsync() =>
        await _authorisation.AuthoriseAsync(Permissions.ManageLookupLists) &&
        await _entitlement.IsAllowedAsync(Capabilities.FormFieldConfiguration);

    /// <summary>
    /// English required, Arabic optional unless the tenant says otherwise — ADR-0003's addendum.
    /// </summary>
    /// <remarks>
    /// Asked through <see cref="IBilingualNamePolicy"/>, the same one-question seam the employee
    /// record and every dimension-engine write path use, so a tenant that insists on Arabic insists
    /// on it here too without this service knowing where the setting lives.
    /// </remarks>
    private async Task<List<RecordError>> ValidateNameAsync(
        BilingualText name,
        string subject,
        LocalizedString englishMissing,
        CancellationToken cancellationToken,
        string fieldEn = "NameEn",
        string fieldAr = "NameAr")
    {
        var errors = new List<RecordError>();

        if (string.IsNullOrWhiteSpace(name.En))
        {
            errors.Add(new RecordError(RecordRule.NameRequired, subject, englishMissing, Field: fieldEn));
        }

        if (string.IsNullOrWhiteSpace(name.Ar) &&
            await _namePolicy.RequiresArabicAsync(cancellationToken))
        {
            errors.Add(new RecordError(
                RecordRule.NameRequired,
                subject,
                S["This tenant requires an Arabic name."],
                Field: fieldAr));
        }

        return errors;
    }

    /// <summary>
    /// Every rule about the set of entries, including the two that make a list safe to bind a field
    /// to.
    /// </summary>
    /// <param name="existing">
    /// What the tenant already holds, or null when the list is being created. The two immutability
    /// rules are statements about the difference between this and the proposed set, so they can
    /// only be checked here.
    /// </param>
    private async Task<List<RecordError>> ValidateItemsAsync(
        IReadOnlyList<LookupListItem> items,
        IReadOnlyList<LookupListItem>? existing,
        CancellationToken cancellationToken)
    {
        var errors = new List<RecordError>();
        var seen = new HashSet<string>(LookupCodes.Comparer);

        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var code = LookupCodes.Normalise(item.Code);
            var field = $"Items[{index}].Code";

            if (!LookupCodes.IsValidItemCode(code))
            {
                errors.Add(new RecordError(
                    RecordRule.CodeFormat,
                    code,
                    S["'{0}' is not a valid entry code. Use letters, digits, hyphen, underscore, dot or slash, up to {1} characters, with no spaces.",
                        code,
                        LookupCodes.MaxLength],
                    Field: field));

                continue;
            }

            if (!seen.Add(code))
            {
                errors.Add(new RecordError(
                    RecordRule.CodeUniqueness,
                    code,
                    S["The entry code '{0}' is used more than once in this list.", code],
                    Field: field));

                continue;
            }

            errors.AddRange(await ValidateNameAsync(
                item.Name,
                code,
                S["The entry '{0}' needs an English name.", code],
                cancellationToken,
                fieldEn: $"Items[{index}].NameEn",
                fieldAr: $"Items[{index}].NameAr"));
        }

        if (existing is null)
        {
            return errors;
        }

        // The rule ADR-0014 exists for, and the one a screen cannot be trusted to enforce on its
        // own: an entry the tenant already holds is a value records may already carry, so it cannot
        // be dropped out of the set. Retiring is the supported way to stop offering it, and the
        // message says so rather than leaving the reader to discover it.
        foreach (var dropped in existing.Where(item => !seen.Contains(LookupCodes.Normalise(item.Code))))
        {
            errors.Add(new RecordError(
                RecordRule.ImmutableOnceInUse,
                dropped.Code,
                S["The entry '{0}' cannot be removed from this list, because a record may already hold it and nothing would report that it no longer resolves. Retire it instead: it stops being offered and stays readable on the records that have it.",
                    dropped.Code]));
        }

        return errors;
    }

    /// <summary>
    /// The entries as they are stored: codes and names trimmed, ordered by sort order then English
    /// name.
    /// </summary>
    /// <remarks>
    /// Ordered on the way in rather than on every read, so that the order a reader is offered is the
    /// order the document holds and an export reproduces it without a second sort that could
    /// disagree. The tie-break on English name is the rule the organisation chart already uses for
    /// siblings, so two things that look like lists behave like each other.
    /// </remarks>
    private static IReadOnlyList<LookupListItem> Ordered(IReadOnlyList<LookupListItem> items) =>
    [
        .. items
            .Select(item => new LookupListItem(
                LookupCodes.Normalise(item.Code),
                Trimmed(item.Name),
                item.IsActive,
                item.SortOrder))
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.Name.En, StringComparer.OrdinalIgnoreCase),
    ];

    /// <summary>
    /// A bilingual name with both halves trimmed.
    /// </summary>
    /// <remarks>
    /// Every write path through a screen trims; the recipe steps did not, and ADR-0003's addendum
    /// records what that cost — a recipe carrying <c>" "</c> stored <c>" "</c>, a later edit through
    /// a screen normalised it to <c>""</c>, and the next re-run of the same unchanged recipe then
    /// failed. Trimming in the service means every path agrees.
    /// </remarks>
    private static BilingualText Trimmed(BilingualText name) =>
        new(name.En.Trim(), name.Ar.Trim());

    private RecordError UnknownList(string lookupListId) => new(
        RecordRule.UnknownLookupList,
        lookupListId,
        S["There is no lookup list with the id '{0}' in this tenant.", lookupListId]);

    private Task RecordAsync(
        LookupListDocument document,
        LookupListState? before,
        LookupListState? after,
        string operation) =>
        _auditTrailManager.RecordEventAsync(new AuditTrailContext<LookupListAuditEvent>(
            FormsAuditTrail.LookupListChanged,
            FormsAuditTrail.Category,
            document.LookupListId,
            userId: null,
            userName: null,
            new LookupListAuditEvent
            {
                LookupListId = document.LookupListId,
                Code = document.Code,
                Before = before,
                After = after,
                Operation = operation,
            }));
}
