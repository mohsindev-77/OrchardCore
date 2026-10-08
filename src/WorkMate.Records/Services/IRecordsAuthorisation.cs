using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using OrchardCore.Modules;
using OrchardCore.Security.Permissions;
using WorkMate.Platform.Services;

namespace WorkMate.Records.Services;

/// <summary>
/// The one place this module decides whether a caller may act, and what "today" is.
/// </summary>
/// <remarks>
/// Deliberately a near-copy of <c>WorkMate.Dimensions.Services.IDimensionAuthorisation</c> rather
/// than a reuse of it. Reusing it would work and would compile — this module already references
/// that one — but it would mean the employee record's permission gate was named for, and owned by,
/// the dimension engine, and a change made there for a dimension-engine reason would land here
/// unannounced.
///
/// <b>This duplication is recognised and is not meant to last.</b> The right end state is one
/// authorisation seam in <c>WorkMate.Platform</c>, which every module takes. Hoisting it means
/// changing every call site in <c>WorkMate.Dimensions</c>, which is its own reviewable change with
/// its own risk and no behaviour attached; doing it inside this slice would bury it. Recorded in
/// the module README so it is a decision somebody took rather than a thing nobody noticed.
/// </remarks>
public interface IRecordsAuthorisation
{
    /// <summary>Whether the current caller holds <paramref name="permission"/>.</summary>
    Task<bool> AuthoriseAsync(Permission permission);

    /// <summary>
    /// Today in the tenant's time zone.
    /// </summary>
    /// <remarks>
    /// Never <c>DateTime.Today</c>. A tenant in Bahrain and a server in Ireland disagree about what
    /// day it is for seven hours out of every twenty-four, and an exit dated by the server's clock
    /// lands on the wrong side of a month end — which is a month's gratuity.
    /// </remarks>
    Task<DateOnly> TodayAsync();
}

/// <inheritdoc />
public sealed class RecordsAuthorisation : IRecordsAuthorisation
{
    private readonly IAuthorizationService _authorizationService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ISystemOperation _systemOperation;
    private readonly ILocalClock _localClock;

    public RecordsAuthorisation(
        IAuthorizationService authorizationService,
        IHttpContextAccessor httpContextAccessor,
        ISystemOperation systemOperation,
        ILocalClock localClock)
    {
        _authorizationService = authorizationService;
        _httpContextAccessor = httpContextAccessor;
        _systemOperation = systemOperation;
        _localClock = localClock;
    }

    /// <summary>
    /// Authorises against the current user, or against an explicitly entered system scope.
    /// </summary>
    /// <remarks>
    /// System authority first, and deliberately: a recipe applied from the admin screen runs inside
    /// a request with a signed-in user, and the step still has to act as the platform rather than as
    /// whoever clicked Import.
    ///
    /// No user and no system scope is refused. The absence of an HTTP context does not grant
    /// authority — that would exempt every background job ever written from every permission check,
    /// by default and invisibly.
    /// </remarks>
    public async Task<bool> AuthoriseAsync(Permission permission)
    {
        if (_systemOperation.IsActive)
        {
            return true;
        }

        var user = _httpContextAccessor.HttpContext?.User;

        return user is not null && await _authorizationService.AuthorizeAsync(user, permission);
    }

    /// <inheritdoc />
    public async Task<DateOnly> TodayAsync() =>
        DateOnly.FromDateTime((await _localClock.GetLocalNowAsync()).DateTime);
}
