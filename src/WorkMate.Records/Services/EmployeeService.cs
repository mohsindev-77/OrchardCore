using Microsoft.Extensions.Localization;
using OrchardCore.AuditTrail.Services;
using OrchardCore.AuditTrail.Services.Models;
using OrchardCore.ContentManagement;
using WorkMate.Core;
using WorkMate.Dimensions.Indexes;
using WorkMate.Dimensions.Services;
using WorkMate.Platform.Services;
using WorkMate.Records.Indexes;
using WorkMate.Records.Models;
using YesSql;

namespace WorkMate.Records.Services;

/// <inheritdoc />
/// <remarks>
/// Every method checks its permission here rather than relying on a controller, so the admin
/// screens, the recipe steps and any future API are held to the same rules by the same code. Every
/// dated write takes its date explicitly and defaults nothing.
///
/// <b>The exit is the method worth reading first.</b> It is the only one that reaches into another
/// module, and it has to: ending employment closes the person's placements and their headships, and
/// both of those live in the dimension engine. It goes through
/// <c>IEmployeeAssignmentService</c> — never the assignment tables, which are internal to that
/// module and are meant to be — exactly as rule 9 requires.
/// </remarks>
internal sealed class EmployeeService : IEmployeeService
{
    private readonly ISession _session;
    private readonly IContentManager _contentManager;
    private readonly IRecordsAuthorisation _authorisation;
    private readonly IBilingualNamePolicy _namePolicy;
    private readonly IAuditTrailManager _auditTrailManager;

    /// <summary>
    /// How an exit closes what the employee holds in the dimension engine.
    /// </summary>
    /// <remarks>
    /// The aggregate's own service, not its tables — rule 9, and the hard boundary that nothing
    /// outside <c>WorkMate.Dimensions</c> reads the link, closure or assignment tables. The tables
    /// are internal types in an internal namespace, so this is a compiler error rather than a
    /// convention.
    /// </remarks>
    private readonly IEmployeeAssignmentService _assignments;

    /// <summary>For naming the units an exit leaves vacant, rather than printing their ids.</summary>
    private readonly IDimensionService _dimensions;

    private readonly IStructureService _structures;

    /// <summary>
    /// Everything that wants to know when an employee is created or changes state.
    /// </summary>
    /// <remarks>
    /// Run inside this scope and this session, so a handler that throws fails the transition. See
    /// <see cref="IEmployeeLifecycleHandler"/>: if payroll cannot record that somebody left, they
    /// have not left as far as this platform is concerned.
    /// </remarks>
    private readonly IEnumerable<IEmployeeLifecycleHandler> _lifecycleHandlers;

    private readonly IStringLocalizer S;

    public EmployeeService(
        ISession session,
        IContentManager contentManager,
        IRecordsAuthorisation authorisation,
        IBilingualNamePolicy namePolicy,
        IAuditTrailManager auditTrailManager,
        IEmployeeAssignmentService assignments,
        IDimensionService dimensions,
        IStructureService structures,
        IEnumerable<IEmployeeLifecycleHandler> lifecycleHandlers,
        IStringLocalizer<EmployeeService> stringLocalizer)
    {
        _session = session;
        _contentManager = contentManager;
        _authorisation = authorisation;
        _namePolicy = namePolicy;
        _auditTrailManager = auditTrailManager;
        _assignments = assignments;
        _dimensions = dimensions;
        _structures = structures;
        _lifecycleHandlers = lifecycleHandlers;
        S = stringLocalizer;
    }

    // ---- create and change ------------------------------------------------------------

    public async Task<RecordResult<EmployeeRecord>> CreateAsync(
        string code,
        BilingualText name,
        DateOnly joinDate,
        EmployeeDetails? details = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (!await _authorisation.AuthoriseAsync(Permissions.ManageEmployees))
        {
            return RecordResult.NotAuthorised<EmployeeRecord>();
        }

        details ??= new EmployeeDetails();
        code = EmployeeCodes.Normalise(code);

        var errors = new List<RecordError>();

        errors.AddRange(await ValidateCodeAsync(code, excludingEmployeeId: null, cancellationToken));
        errors.AddRange(await ValidateNameAsync(name, code, cancellationToken));
        errors.AddRange(ValidateDates(code, joinDate, details.DateOfBirth));
        errors.AddRange(await ValidateLineManagerAsync(
            code, employeeId: null, details.LineManagerEmployeeId, cancellationToken));

        if (errors.Count > 0)
        {
            return RecordResult.Failed<EmployeeRecord>(errors);
        }

        var item = await _contentManager.NewAsync(EmployeeFieldNames.ContentType);

        item.Alter<EmployeePart>(part =>
        {
            part.EmployeeCode = code;
            part.NameEn = name.En;
            part.NameAr = name.Ar;
            part.JoinDate = joinDate;

            // Always prospective, whatever the join date, and dated from the join date so that the
            // record carries a date somebody stated rather than the day it was keyed in. Activating
            // them is a separate decision — see the interface.
            part.Status = EmploymentStatus.Prospective;
            part.StatusEffectiveFrom = joinDate;

            ApplyDetails(part, details);
        });

        await _contentManager.UpdateAsync(item);

        // The handler's rules run here, so every path into an employee record — this one, an
        // import, the API, GraphQL — is held to the same standard.
        var validated = await _contentManager.ValidateAsync(item);

        if (!validated.Succeeded)
        {
            return RecordResult.Failed<EmployeeRecord>(
            [
                .. validated.Errors.Select(error => new RecordError(
                    RecordRule.CodeFormat,
                    code,
                    new LocalizedString(error.ErrorMessage ?? string.Empty, error.ErrorMessage ?? string.Empty))),
            ]);
        }

        await _contentManager.CreateAsync(item, VersionOptions.Published);

        // Flushed so the employee's index row exists before anything reads it back — the lifecycle
        // handlers below, and in a recipe the next row's line-manager lookup.
        await _session.FlushAsync(cancellationToken);

        var part = item.Get<EmployeePart>(EmployeeFieldNames.PartName)!;

        await RecordAsync(item.ContentItemId, code, "Created", joinDate, before: null, after: part);

        foreach (var handler in _lifecycleHandlers)
        {
            await handler.EmployeeCreatedAsync(
                new EmployeeCreated(item.ContentItemId, code, name.En, name.Ar, joinDate),
                cancellationToken);
        }

        return RecordResult.Success(ToRecord(item.ContentItemId, part));
    }

    public async Task<RecordResult<EmployeeRecord>> UpdateAsync(
        string employeeId,
        BilingualText name,
        EmployeeDetails details,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(details);

        if (!await _authorisation.AuthoriseAsync(Permissions.ManageEmployees))
        {
            return RecordResult.NotAuthorised<EmployeeRecord>();
        }

        var item = await LoadAsync(employeeId, cancellationToken);

        if (item is null)
        {
            return RecordResult.Failed<EmployeeRecord>(UnknownEmployee(employeeId));
        }

        var part = item.Get<EmployeePart>(EmployeeFieldNames.PartName)!;
        var before = EmployeeState.Of(part);

        var errors = new List<RecordError>();

        errors.AddRange(await ValidateNameAsync(name, part.EmployeeCode, cancellationToken));
        errors.AddRange(ValidateDates(part.EmployeeCode, part.JoinDate, details.DateOfBirth));
        errors.AddRange(await ValidateLineManagerAsync(
            part.EmployeeCode, employeeId, details.LineManagerEmployeeId, cancellationToken));

        if (errors.Count > 0)
        {
            return RecordResult.Failed<EmployeeRecord>(errors);
        }

        item.Alter<EmployeePart>(editing =>
        {
            editing.NameEn = name.En;
            editing.NameAr = name.Ar;

            ApplyDetails(editing, details);
        });

        await _contentManager.UpdateAsync(item);
        await _contentManager.PublishAsync(item);

        var after = item.Get<EmployeePart>(EmployeeFieldNames.PartName)!;

        await RecordAsync(employeeId, after.EmployeeCode, "Updated", effectiveOn: null, before, after);

        return RecordResult.Success(ToRecord(employeeId, after));
    }

    public async Task<RecordResult<EmployeeRecord>> CorrectJoinDateAsync(
        string employeeId,
        DateOnly joinDate,
        CancellationToken cancellationToken = default)
    {
        // The lifecycle permission, not the editing one. Moving the join date moves gratuity, leave
        // accrual and probation with it; that is not the same capability as fixing a phone number.
        if (!await _authorisation.AuthoriseAsync(Permissions.ChangeEmploymentStatus))
        {
            return RecordResult.NotAuthorised<EmployeeRecord>();
        }

        var item = await LoadAsync(employeeId, cancellationToken);

        if (item is null)
        {
            return RecordResult.Failed<EmployeeRecord>(UnknownEmployee(employeeId));
        }

        var part = item.Get<EmployeePart>(EmployeeFieldNames.PartName)!;
        var before = EmployeeState.Of(part);

        var errors = ValidateDates(part.EmployeeCode, joinDate, part.DateOfBirth);

        if (errors.Count > 0)
        {
            return RecordResult.Failed<EmployeeRecord>(errors);
        }

        item.Alter<EmployeePart>(editing => editing.JoinDate = joinDate);

        await _contentManager.UpdateAsync(item);
        await _contentManager.PublishAsync(item);

        var after = item.Get<EmployeePart>(EmployeeFieldNames.PartName)!;

        await RecordAsync(
            employeeId,
            after.EmployeeCode,
            "JoinDateCorrected",
            joinDate,
            before,
            after,
            eventName: RecordsAuditTrail.JoinDateCorrected);

        return RecordResult.Success(ToRecord(employeeId, after));
    }

    // ---- lifecycle --------------------------------------------------------------------

    public Task<RecordResult<EmployeeRecord>> ActivateAsync(
        string employeeId, DateOnly effectiveFrom, CancellationToken cancellationToken = default) =>
        TransitionAsync(employeeId, EmploymentStatus.Active, effectiveFrom, cancellationToken);

    public Task<RecordResult<EmployeeRecord>> PutOnLeaveAsync(
        string employeeId, DateOnly effectiveFrom, CancellationToken cancellationToken = default) =>
        TransitionAsync(employeeId, EmploymentStatus.OnLeave, effectiveFrom, cancellationToken);

    public Task<RecordResult<EmployeeRecord>> SuspendAsync(
        string employeeId, DateOnly effectiveFrom, CancellationToken cancellationToken = default) =>
        TransitionAsync(employeeId, EmploymentStatus.Suspended, effectiveFrom, cancellationToken);

    public Task<RecordResult<EmployeeRecord>> ReinstateAsync(
        string employeeId, DateOnly effectiveFrom, CancellationToken cancellationToken = default) =>
        TransitionAsync(employeeId, EmploymentStatus.Active, effectiveFrom, cancellationToken);

    public async Task<RecordResult<EmployeeExitPlan>> PlanExitAsync(
        string employeeId,
        DateOnly lastDay,
        CancellationToken cancellationToken = default)
    {
        if (!await _authorisation.AuthoriseAsync(Permissions.ChangeEmploymentStatus))
        {
            return RecordResult.NotAuthorised<EmployeeExitPlan>();
        }

        var item = await LoadAsync(employeeId, cancellationToken);

        if (item is null)
        {
            return RecordResult.Failed<EmployeeExitPlan>(UnknownEmployee(employeeId));
        }

        var part = item.Get<EmployeePart>(EmployeeFieldNames.PartName)!;
        var errors = ValidateTransition(part, EmploymentStatus.Exited, lastDay.AddDays(1));

        if (errors.Count > 0)
        {
            return RecordResult.Failed<EmployeeExitPlan>(errors);
        }

        // As at the last day, not today. An exit dated in the past has to report what was running
        // then, and one dated in the future has to report what will still be running by then —
        // asking about today would answer a different question in both directions.
        var assignments = await _assignments.GetAllAxesAsync(employeeId, lastDay, cancellationToken);
        var vacated = await DescribeHeadshipsAsync(employeeId, lastDay, cancellationToken);

        return RecordResult.Success(new EmployeeExitPlan(
            ToRecord(employeeId, part), lastDay, assignments, vacated));
    }

    public async Task<RecordResult<EmployeeExit>> ExitAsync(
        string employeeId,
        DateOnly lastDay,
        CancellationToken cancellationToken = default)
    {
        if (!await _authorisation.AuthoriseAsync(Permissions.ChangeEmploymentStatus))
        {
            return RecordResult.NotAuthorised<EmployeeExit>();
        }

        var item = await LoadAsync(employeeId, cancellationToken);

        if (item is null)
        {
            return RecordResult.Failed<EmployeeExit>(UnknownEmployee(employeeId));
        }

        var part = item.Get<EmployeePart>(EmployeeFieldNames.PartName)!;
        var before = EmployeeState.Of(part);
        var exitedFrom = lastDay.AddDays(1);

        var errors = ValidateTransition(part, EmploymentStatus.Exited, exitedFrom);

        if (errors.Count > 0)
        {
            return RecordResult.Failed<EmployeeExit>(errors);
        }

        // Described before anything is closed, because afterwards there is nothing left to name:
        // the whole point of the list is to tell somebody which units are now without a head.
        var vacated = await DescribeHeadshipsAsync(employeeId, lastDay, cancellationToken);

        // Every axis, not only the primary one. A person placed on an organisation axis and a cost
        // axis who was closed on one of them would keep being charged somewhere after they left.
        var axes = await _assignments.GetAllAxesAsync(employeeId, lastDay, cancellationToken);
        var assignmentsClosed = 0;

        foreach (var structureId in axes
            .Select(assignment => assignment.StructureId)
            .Distinct(StringComparer.Ordinal))
        {
            var ended = await _assignments.EndAsync(employeeId, structureId, lastDay, cancellationToken);

            if (!ended.Succeeded)
            {
                return RecordResult.Failed<EmployeeExit>(FromDimensionErrors(part.EmployeeCode, ended.Errors));
            }

            assignmentsClosed += ended.Value;
        }

        var headshipsEnded = await _assignments.EndHeadshipsOfAsync(employeeId, lastDay, cancellationToken);

        if (!headshipsEnded.Succeeded)
        {
            return RecordResult.Failed<EmployeeExit>(FromDimensionErrors(part.EmployeeCode, headshipsEnded.Errors));
        }

        item.Alter<EmployeePart>(editing =>
        {
            editing.Status = EmploymentStatus.Exited;
            editing.StatusEffectiveFrom = exitedFrom;
        });

        await _contentManager.UpdateAsync(item);
        await _contentManager.PublishAsync(item);
        await _session.FlushAsync(cancellationToken);

        var after = item.Get<EmployeePart>(EmployeeFieldNames.PartName)!;

        await RecordAsync(
            employeeId,
            after.EmployeeCode,
            "Exited",
            exitedFrom,
            before,
            after,
            eventName: RecordsAuditTrail.EmploymentStatusChanged,
            assignmentsClosed: assignmentsClosed,
            headshipsClosed: headshipsEnded.Value);

        foreach (var handler in _lifecycleHandlers)
        {
            await handler.EmployeeStatusChangedAsync(
                new EmployeeStatusChanged(
                    employeeId,
                    after.EmployeeCode,
                    before.Status,
                    EmploymentStatus.Exited,
                    exitedFrom,
                    lastDay,
                    assignmentsClosed,
                    headshipsEnded.Value),
                cancellationToken);
        }

        return RecordResult.Success(new EmployeeExit(
            ToRecord(employeeId, after), lastDay, assignmentsClosed, headshipsEnded.Value, vacated));
    }

    // ---- reads ------------------------------------------------------------------------

    public async Task<EmployeeRecord?> GetAsync(
        string employeeId,
        CancellationToken cancellationToken = default)
    {
        var item = await LoadAsync(employeeId, cancellationToken);

        return item is null ? null : ToRecord(employeeId, item.Get<EmployeePart>(EmployeeFieldNames.PartName)!);
    }

    public async Task<EmployeeRecord?> GetByCodeAsync(
        string code,
        CancellationToken cancellationToken = default)
    {
        var normalised = EmployeeCodes.Normalise(code).ToUpperInvariant();

        var row = await _session
            .QueryIndex<EmployeeIndex>(index => index.CodeUpper == normalised && index.Latest)
            .FirstOrDefaultAsync(cancellationToken);

        return row is null ? null : ToRecord(row);
    }

    public async Task<Page<EmployeeRecord>> ListAsync(
        string? search = null,
        EmploymentStatus? status = null,
        int skip = 0,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        var query = _session.QueryIndex<EmployeeIndex>(index => index.Latest);

        if (status is not null)
        {
            var name = status.Value.ToString();
            query = query.Where(index => index.Status == name);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            // Contains rather than starts-with, because the thing a person remembers about an
            // employee is as often the middle of their name as the start of it, and a code is as
            // often quoted without its prefix. Three columns so an Arabic reader searching in
            // Arabic finds the same people an English reader finds in English.
            var term = search.Trim();

            query = query.Where(index =>
                index.Code.Contains(term) ||
                index.NameEn.Contains(term) ||
                index.NameAr.Contains(term));
        }

        var total = await query.CountAsync(cancellationToken);

        var rows = await query
            .OrderBy(index => index.NameEn)
            .ThenBy(index => index.Code)
            .Skip(skip)
            .Take(take)
            .ListAsync(cancellationToken);

        return new Page<EmployeeRecord>([.. rows.Select(ToRecord)], total, skip, take);
    }

    // ---- the shared transition --------------------------------------------------------

    /// <summary>
    /// Every lifecycle move except the exit, which closes placements and headships and so has its
    /// own method.
    /// </summary>
    /// <remarks>
    /// One implementation rather than four, because the four differ only in the state they move to:
    /// the permission, the dated-write rule, the transition table, the audit entry and the event
    /// are the same for all of them. Four copies would be four chances for one of them to forget
    /// the event.
    /// </remarks>
    private async Task<RecordResult<EmployeeRecord>> TransitionAsync(
        string employeeId,
        EmploymentStatus to,
        DateOnly effectiveFrom,
        CancellationToken cancellationToken)
    {
        if (!await _authorisation.AuthoriseAsync(Permissions.ChangeEmploymentStatus))
        {
            return RecordResult.NotAuthorised<EmployeeRecord>();
        }

        var item = await LoadAsync(employeeId, cancellationToken);

        if (item is null)
        {
            return RecordResult.Failed<EmployeeRecord>(UnknownEmployee(employeeId));
        }

        var part = item.Get<EmployeePart>(EmployeeFieldNames.PartName)!;
        var before = EmployeeState.Of(part);

        var errors = ValidateTransition(part, to, effectiveFrom);

        if (errors.Count > 0)
        {
            return RecordResult.Failed<EmployeeRecord>(errors);
        }

        item.Alter<EmployeePart>(editing =>
        {
            editing.Status = to;
            editing.StatusEffectiveFrom = effectiveFrom;
        });

        await _contentManager.UpdateAsync(item);
        await _contentManager.PublishAsync(item);
        await _session.FlushAsync(cancellationToken);

        var after = item.Get<EmployeePart>(EmployeeFieldNames.PartName)!;

        await RecordAsync(
            employeeId,
            after.EmployeeCode,
            to.ToString(),
            effectiveFrom,
            before,
            after,
            eventName: RecordsAuditTrail.EmploymentStatusChanged);

        foreach (var handler in _lifecycleHandlers)
        {
            await handler.EmployeeStatusChangedAsync(
                new EmployeeStatusChanged(
                    employeeId, after.EmployeeCode, before.Status, to, effectiveFrom),
                cancellationToken);
        }

        return RecordResult.Success(ToRecord(employeeId, after));
    }

    // ---- rules ------------------------------------------------------------------------

    private List<RecordError> ValidateTransition(EmployeePart part, EmploymentStatus to, DateOnly effectiveFrom)
    {
        var errors = new List<RecordError>();

        if (!EmployeeLifecycle.Permits(part.Status, to))
        {
            errors.Add(new RecordError(
                RecordRule.TransitionNotPermitted,
                part.EmployeeCode,
                S["An employee who is {0} cannot become {1}.", EmployeeStatusNames.Of(S, part.Status), EmployeeStatusNames.Of(S, to)]));

            return errors;
        }

        // A transition dated before the state it replaces began would make the record claim two
        // things at once about the same day. This engine keeps history by adding periods, never by
        // overwriting one, so the only honest answer is to refuse and say which date is in the way.
        if (effectiveFrom < part.StatusEffectiveFrom)
        {
            errors.Add(new RecordError(
                RecordRule.TransitionOutOfOrder,
                part.EmployeeCode,
                S["This employee has been {0} since {1}, so a change cannot take effect on {2}.",
                    EmployeeStatusNames.Of(S, part.Status),
                    part.StatusEffectiveFrom,
                    effectiveFrom],
                Field: "EffectiveFrom"));
        }

        if (effectiveFrom < part.JoinDate)
        {
            errors.Add(new RecordError(
                RecordRule.DateOutOfOrder,
                part.EmployeeCode,
                S["This employee joined on {0}, so a change cannot take effect on {1}.",
                    part.JoinDate,
                    effectiveFrom],
                Field: "EffectiveFrom"));
        }

        return errors;
    }

    private async Task<List<RecordError>> ValidateCodeAsync(
        string code,
        string? excludingEmployeeId,
        CancellationToken cancellationToken)
    {
        var errors = new List<RecordError>();

        if (!EmployeeCodes.IsValid(code))
        {
            errors.Add(new RecordError(
                RecordRule.CodeFormat,
                code,
                S["'{0}' is not a valid employee code. Use up to {1} letters, digits, hyphens, underscores, dots or slashes, with no spaces.",
                    code,
                    EmployeeCodes.MaxLength],
                Field: nameof(EmployeePart.EmployeeCode)));

            return errors;
        }

        var upper = code.ToUpperInvariant();

        var clash = await _session
            .QueryIndex<EmployeeIndex>(index => index.CodeUpper == upper && index.Latest)
            .FirstOrDefaultAsync(cancellationToken);

        if (clash is not null &&
            !string.Equals(clash.ContentItemId, excludingEmployeeId, StringComparison.Ordinal))
        {
            errors.Add(new RecordError(
                RecordRule.CodeUniqueness,
                code,
                S["The employee code '{0}' is already used by {1}. Codes stay unique against employees who have left, because payroll history resolves through them.",
                    clash.Code,
                    clash.NameEn],
                Field: nameof(EmployeePart.EmployeeCode)));
        }

        return errors;
    }

    /// <summary>
    /// English required, Arabic optional unless the tenant says otherwise — ADR-0003's addendum.
    /// </summary>
    /// <remarks>
    /// Asked through <see cref="IBilingualNamePolicy"/>, the same one-question seam every write path
    /// in <c>WorkMate.Dimensions</c> uses, so the employee record and the organisation chart cannot
    /// end up disagreeing about whether this tenant insists on Arabic.
    /// </remarks>
    private async Task<List<RecordError>> ValidateNameAsync(
        BilingualText name,
        string code,
        CancellationToken cancellationToken)
    {
        var errors = new List<RecordError>();

        if (string.IsNullOrWhiteSpace(name.En))
        {
            errors.Add(new RecordError(
                RecordRule.NameRequired,
                code,
                S["An employee needs an English name."],
                Field: nameof(EmployeePart.NameEn)));
        }

        if (string.IsNullOrWhiteSpace(name.Ar) &&
            await _namePolicy.RequiresArabicAsync(cancellationToken))
        {
            errors.Add(new RecordError(
                RecordRule.NameRequired,
                code,
                S["This tenant requires an Arabic name."],
                Field: nameof(EmployeePart.NameAr)));
        }

        return errors;
    }

    private List<RecordError> ValidateDates(string code, DateOnly joinDate, DateOnly? dateOfBirth)
    {
        var errors = new List<RecordError>();

        if (joinDate == default)
        {
            errors.Add(new RecordError(
                RecordRule.EffectiveDateRequired,
                code,
                S["An employee needs a join date. Nothing is defaulted on a write."],
                Field: nameof(EmployeePart.JoinDate)));
        }

        if (dateOfBirth is not null && joinDate != default && dateOfBirth >= joinDate)
        {
            errors.Add(new RecordError(
                RecordRule.DateOutOfOrder,
                code,
                S["A date of birth of {0} is not before the join date of {1}.", dateOfBirth.Value, joinDate],
                Field: nameof(EmployeePart.DateOfBirth)));
        }

        return errors;
    }

    /// <summary>
    /// That a line manager exists, and that the reporting line does not loop back round to this
    /// employee.
    /// </summary>
    /// <remarks>
    /// The cycle check walks up rather than down, which is the cheap direction — each employee has
    /// one manager and may have hundreds of reports — and it is bounded by the number of employees
    /// so that a loop already in the data cannot hang the check that is trying to find it.
    /// </remarks>
    private async Task<List<RecordError>> ValidateLineManagerAsync(
        string code,
        string? employeeId,
        string? lineManagerEmployeeId,
        CancellationToken cancellationToken)
    {
        var errors = new List<RecordError>();

        if (string.IsNullOrWhiteSpace(lineManagerEmployeeId))
        {
            return errors;
        }

        if (employeeId is not null &&
            string.Equals(lineManagerEmployeeId, employeeId, StringComparison.Ordinal))
        {
            errors.Add(new RecordError(
                RecordRule.ReportingLineCycle,
                code,
                S["An employee cannot report to themselves."],
                Field: nameof(EmployeePart.LineManagerEmployeeId)));

            return errors;
        }

        var manager = await _session
            .QueryIndex<EmployeeIndex>(index =>
                index.ContentItemId == lineManagerEmployeeId && index.Latest)
            .FirstOrDefaultAsync(cancellationToken);

        if (manager is null)
        {
            errors.Add(new RecordError(
                RecordRule.UnknownEmployee,
                code,
                S["There is no employee with the id '{0}' to report to.", lineManagerEmployeeId],
                Field: nameof(EmployeePart.LineManagerEmployeeId)));

            return errors;
        }

        if (employeeId is null)
        {
            // A brand-new employee is not yet anybody's manager, so no chain through them exists.
            return errors;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal) { employeeId };
        var walker = manager;

        while (walker is not null && !string.IsNullOrEmpty(walker.LineManagerEmployeeId))
        {
            if (!seen.Add(walker.ContentItemId))
            {
                // A loop that was already in the data, not one this edit would create. Reported
                // rather than ignored: it is the same defect and this is where it surfaces.
                break;
            }

            if (string.Equals(walker.LineManagerEmployeeId, employeeId, StringComparison.Ordinal))
            {
                errors.Add(new RecordError(
                    RecordRule.ReportingLineCycle,
                    code,
                    S["{0} already reports to this employee, directly or indirectly, so they cannot also be their manager.",
                        manager.NameEn],
                    Field: nameof(EmployeePart.LineManagerEmployeeId)));

                break;
            }

            var next = walker.LineManagerEmployeeId;

            walker = await _session
                .QueryIndex<EmployeeIndex>(index => index.ContentItemId == next && index.Latest)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return errors;
    }

    // ---- plumbing ---------------------------------------------------------------------

    private static void ApplyDetails(EmployeePart part, EmployeeDetails details)
    {
        part.DateOfBirth = details.DateOfBirth;
        part.NationalityCode = details.NationalityCode?.Trim() ?? string.Empty;
        part.Gender = details.Gender;
        part.LineManagerEmployeeId = details.LineManagerEmployeeId?.Trim() ?? string.Empty;
        part.PhotoPath = details.PhotoPath?.Trim() ?? string.Empty;
    }

    private async Task<ContentItem?> LoadAsync(string employeeId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var item = await _contentManager.GetAsync(employeeId, VersionOptions.Latest);

        return item is not null && item.Has<EmployeePart>() ? item : null;
    }

    /// <summary>
    /// The units this employee heads on a date, named rather than identified.
    /// </summary>
    /// <remarks>
    /// Resolves the record and the structure for each, because the caller is a screen telling
    /// somebody which departments are about to lose their head, and a list of content item ids
    /// tells them nothing they can act on.
    /// </remarks>
    private async Task<IReadOnlyList<VacatedUnit>> DescribeHeadshipsAsync(
        string employeeId,
        DateOnly asAt,
        CancellationToken cancellationToken)
    {
        var headships = await _assignments.GetHeadshipsOfAsync(employeeId, asAt, cancellationToken);

        if (headships.Count == 0)
        {
            return [];
        }

        var vacated = new List<VacatedUnit>();

        foreach (var headship in headships)
        {
            var record = await _dimensions.GetAsync(headship.RecordId, asAt, cancellationToken);
            var structure = await _structures.GetAsync(headship.StructureId, cancellationToken);

            vacated.Add(new VacatedUnit(
                headship.StructureId,
                structure?.Code ?? headship.StructureId,
                headship.RecordId,
                record?.Code ?? headship.RecordId,
                record?.NameEn ?? headship.RecordId,
                record?.NameAr ?? string.Empty));
        }

        return vacated;
    }

    private Task RecordAsync(
        string employeeId,
        string code,
        string operation,
        DateOnly? effectiveOn,
        EmployeeState? before,
        EmployeePart? after,
        string eventName = RecordsAuditTrail.EmployeeChanged,
        int assignmentsClosed = 0,
        int headshipsClosed = 0) =>
        _auditTrailManager.RecordEventAsync(new AuditTrailContext<EmployeeAuditEvent>(
            eventName,
            RecordsAuditTrail.Category,
            employeeId,
            userId: null,
            userName: null,
            new EmployeeAuditEvent
            {
                EmployeeId = employeeId,
                Code = code,
                Before = before,
                After = after is null ? null : EmployeeState.Of(after),
                Operation = operation,
                EffectiveOn = effectiveOn,
                AssignmentsClosed = assignmentsClosed,
                HeadshipsClosed = headshipsClosed,
            }));

    private RecordError UnknownEmployee(string employeeId) => new(
        RecordRule.UnknownEmployee,
        employeeId,
        S["There is no employee with the id '{0}' in this tenant.", employeeId]);

    /// <summary>
    /// Dimension-engine failures, restated in this module's vocabulary.
    /// </summary>
    /// <remarks>
    /// Translated rather than passed through, because a caller of <c>IEmployeeService</c> branches
    /// on <see cref="RecordRule"/> and should not have to also know <c>DimensionRule</c> exists.
    /// The localised sentence is carried across unchanged — it is the one the other module wrote
    /// for the user, and rewording it here would be a second description of the same failure.
    /// </remarks>
    private static IReadOnlyList<RecordError> FromDimensionErrors(
        string code,
        IReadOnlyList<DimensionError> errors) =>
        [.. errors.Select(error => new RecordError(RecordRule.TransitionNotPermitted, code, error.Message))];

    private static EmployeeRecord ToRecord(string employeeId, EmployeePart part) => new(
        employeeId,
        part.EmployeeCode,
        part.NameEn,
        part.NameAr,
        part.JoinDate,
        part.Status,
        part.StatusEffectiveFrom,
        part.DateOfBirth,
        part.NationalityCode,
        part.Gender,
        part.LineManagerEmployeeId,
        part.PhotoPath);

    /// <summary>
    /// An index row back to a record, for the list and the lookups, which must not load a content
    /// item per employee to draw a page.
    /// </summary>
    /// <remarks>
    /// Two fields are not on the index and are not on this path: date of birth and nationality are
    /// sensitive and are not shown on a list, and the photo path is not either. A caller that needs
    /// them asks <see cref="GetAsync"/>, which loads the item — which is also where
    /// <c>ViewEmployeeSensitiveData</c> gets its chance to apply.
    /// </remarks>
    private static EmployeeRecord ToRecord(EmployeeIndex row) => new(
        row.ContentItemId,
        row.Code,
        row.NameEn,
        row.NameAr,
        EffectiveDates.FromColumn(row.JoinDate),
        Enum.TryParse<EmploymentStatus>(row.Status, out var status) ? status : EmploymentStatus.Prospective,
        EffectiveDates.FromColumn(row.StatusEffectiveFrom),
        DateOfBirth: null,
        NationalityCode: string.Empty,
        Gender: Gender.Unspecified,
        row.LineManagerEmployeeId,
        PhotoPath: string.Empty);
}
