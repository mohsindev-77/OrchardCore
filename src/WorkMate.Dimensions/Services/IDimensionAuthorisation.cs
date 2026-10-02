using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using OrchardCore.Modules;
using OrchardCore.Security.Permissions;
using WorkMate.Platform.Services;

namespace WorkMate.Dimensions.Services;

/// <summary>
/// The one place this module decides whether a caller may do something, and the one place it
/// decides what "today" is.
/// </summary>
/// <remarks>
/// Both are here together because every service method needs at least one of them and both have
/// exactly one right answer that must not be re-decided per service.
/// </remarks>
public interface IDimensionAuthorisation
{
    /// <summary>Whether the current caller holds <paramref name="permission"/>.</summary>
    Task<bool> AuthoriseAsync(Permission permission);

    /// <summary>
    /// Today in the tenant's time zone. Every read in this module defaults its effective date to
    /// this, and nothing uses <c>DateTime.Today</c>: a tenant in Bahrain and a server in Ireland
    /// disagree about what day it is for seven hours of every day, and a transfer dated by the
    /// server's clock lands on the wrong side of a month end.
    /// </summary>
    Task<DateOnly> TodayAsync();
}

/// <inheritdoc />
public sealed class DimensionAuthorisation : IDimensionAuthorisation
{
    private readonly IAuthorizationService _authorizationService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ISystemOperation _systemOperation;
    private readonly ILocalClock _localClock;

    public DimensionAuthorisation(
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
    /// System authority is checked first and deliberately: a recipe applied through the admin UI
    /// runs inside a request with a signed-in user, and the step still needs to act as the
    /// platform rather than as whoever clicked Import.
    ///
    /// Everything else needs a user. The absence of an HTTP context does not grant authority —
    /// that rule was considered and rejected, because it would exempt every background job ever
    /// written from every permission check by default. No user and no system scope is refused.
    /// </remarks>
    public async Task<bool> AuthoriseAsync(Permission permission)
    {
        if (_systemOperation.IsActive)
        {
            return true;
        }

        var user = _httpContextAccessor.HttpContext?.User;

        if (user is null)
        {
            return false;
        }

        return await _authorizationService.AuthorizeAsync(user, permission);
    }

    /// <inheritdoc />
    public async Task<DateOnly> TodayAsync() =>
        DateOnly.FromDateTime((await _localClock.GetLocalNowAsync()).DateTime);
}
