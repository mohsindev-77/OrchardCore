using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using OrchardCore.Environment.Shell;
using OrchardCore.Modules;
using OrchardCore.Security.Permissions;
using WorkMate.Dimensions.Services;
using WorkMate.Platform.Services;
using Xunit;

namespace WorkMate.Dimensions.Tests;

/// <summary>
/// Who is allowed to do something, and what happens when there is nobody to ask.
/// </summary>
/// <remarks>
/// The case that matters most here is the third one. An earlier version of this service treated
/// the absence of an HTTP context as full authority, on the reasoning that recipes and
/// background tasks have no user to check. Review rejected it: that rule would exempt every
/// background job ever added to this product from every permission check, by default, with
/// nothing in the code saying so. These tests pin the rule that replaced it — no user and no
/// system scope is refused — and the opt-in that makes the legitimate callers work.
/// </remarks>
public sealed class DimensionAuthorisationTests
{
    [Fact]
    public async Task AUserWhoHoldsThePermissionIsAllowed()
    {
        var subject = Build(user: SignedIn(), grants: true, out _);

        (await subject.AuthoriseAsync(Permissions.ManageDimensionTypes)).Should().BeTrue();
    }

    [Fact]
    public async Task AUserWhoDoesNotHoldThePermissionIsRefused()
    {
        var subject = Build(user: SignedIn(), grants: false, out _);

        (await subject.AuthoriseAsync(Permissions.ManageDimensionTypes)).Should().BeFalse();
    }

    [Fact]
    public async Task NoUserAndNoSystemScopeIsRefused()
    {
        // A background task that forgot to declare itself, a job, a stray call from a scope with
        // no request behind it. None of those is authority.
        var subject = Build(user: null, grants: false, out _);

        (await subject.AuthoriseAsync(Permissions.ManageDimensionTypes)).Should().BeFalse(
            "the absence of a user is not evidence of authority");
    }

    [Fact]
    public async Task NoUserInsideASystemScopeIsAllowed()
    {
        var subject = Build(user: null, grants: false, out var system);

        using (system.Begin("a background task that genuinely runs as the platform"))
        {
            (await subject.AuthoriseAsync(Permissions.ManageDimensionTypes)).Should().BeTrue();
        }
    }

    [Fact]
    public async Task AuthorityIsRefusedAgainOnceTheSystemScopeCloses()
    {
        var subject = Build(user: null, grants: false, out var system);

        using (system.Begin("a recipe step"))
        {
        }

        (await subject.AuthoriseAsync(Permissions.ManageDimensionTypes)).Should().BeFalse();
    }

    [Fact]
    public async Task ASystemScopeInsideARequestActsAsThePlatformNotAsTheSignedInUser()
    {
        // A recipe imported through the admin UI runs inside a request, as whoever clicked
        // Import. The step still has to act as the platform: the person importing a configuration
        // package is not necessarily the person who may create dimension types one at a time.
        var subject = Build(user: SignedIn(), grants: false, out var system);

        using (system.Begin("applying an imported recipe"))
        {
            (await subject.AuthoriseAsync(Permissions.ManageDimensionTypes)).Should().BeTrue();
        }

        (await subject.AuthoriseAsync(Permissions.ManageDimensionTypes)).Should().BeFalse(
            "outside the step, the signed-in user is checked normally");
    }

    private static DimensionAuthorisation Build(
        ClaimsPrincipal? user,
        bool grants,
        out ISystemOperation systemOperation)
    {
        systemOperation = new SystemOperation(
            new ShellSettings { Name = "acme-prod" },
            NullLogger<SystemOperation>.Instance);

        var accessor = new HttpContextAccessor();

        if (user is not null)
        {
            accessor.HttpContext = new DefaultHttpContext { User = user };
        }

        return new DimensionAuthorisation(
            new StubAuthorizationService(grants),
            accessor,
            systemOperation,
            new StubLocalClock());
    }

    private static ClaimsPrincipal SignedIn() =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Name, "aisha")], "test"));

    private sealed class StubAuthorizationService : IAuthorizationService
    {
        private readonly bool _grants;

        public StubAuthorizationService(bool grants) => _grants = grants;

        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user,
            object? resource,
            IEnumerable<IAuthorizationRequirement> requirements) =>
            Task.FromResult(_grants ? AuthorizationResult.Success() : AuthorizationResult.Failed());

        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user,
            object? resource,
            string policyName) =>
            Task.FromResult(_grants ? AuthorizationResult.Success() : AuthorizationResult.Failed());
    }

    private sealed class StubLocalClock : ILocalClock
    {
        public Task<DateTimeOffset> GetLocalNowAsync() =>
            Task.FromResult(new DateTimeOffset(2026, 3, 16, 9, 0, 0, TimeSpan.FromHours(3)));

        public Task<ITimeZone> GetLocalTimeZoneAsync() => throw new NotSupportedException();

        public Task<DateTimeOffset> ConvertToLocalAsync(DateTimeOffset dateTimeOffset) =>
            throw new NotSupportedException();

        public Task<DateTime> ConvertToUtcAsync(DateTime dateTime) => throw new NotSupportedException();
    }
}
