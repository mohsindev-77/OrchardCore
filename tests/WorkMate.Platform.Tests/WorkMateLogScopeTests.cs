using System.Diagnostics;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using OrchardCore.Environment.Shell;
using WorkMate.Platform.Logging;
using Xunit;

namespace WorkMate.Platform.Tests;

/// <summary>
/// Specification section 9: "Structured logging with tenant, user and correlation id on every
/// line." These assert the three are actually in the scope, rather than that the code that
/// intends to put them there has been written.
/// </summary>
public sealed class WorkMateLogScopeTests
{
    [Fact]
    public void TheScopeCarriesTenantUserAndCorrelationId()
    {
        var logger = new ScopeRecordingLogger();
        var scope = new WorkMateLogScope(new ShellSettings { Name = "gulf-trading-prod" });

        using (scope.Begin(logger, Authenticated("aisha"), "abc123"))
        {
        }

        var state = logger.Scopes.Should().ContainSingle().Subject;

        state.Tenant.Should().Be("gulf-trading-prod");
        state.User.Should().Be("aisha");
        state.CorrelationId.Should().Be("abc123");
    }

    [Fact]
    public void AnAnonymousCallerIsRecordedAsAnonymousRatherThanAsNothing()
    {
        // A blank user field is indistinguishable from a field somebody forgot to populate.
        var logger = new ScopeRecordingLogger();
        var scope = new WorkMateLogScope(new ShellSettings { Name = "acme-config" });

        using (scope.Begin(logger, new ClaimsPrincipal(new ClaimsIdentity())))
        {
        }

        logger.Scopes.Single().User.Should().Be("(anonymous)");
    }

    [Fact]
    public void ANullUserIsAlsoAnonymous()
    {
        var logger = new ScopeRecordingLogger();
        var scope = new WorkMateLogScope(new ShellSettings { Name = "acme-config" });

        using (scope.Begin(logger, user: null))
        {
        }

        logger.Scopes.Single().User.Should().Be("(anonymous)");
    }

    [Fact]
    public void ACorrelationIdIsGeneratedWhenNoneIsSupplied()
    {
        var logger = new ScopeRecordingLogger();
        var scope = new WorkMateLogScope(new ShellSettings { Name = "acme-config" });

        using (scope.Begin(logger, Authenticated("aisha")))
        {
        }

        logger.Scopes.Single().CorrelationId.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void TheCorrelationIdFollowsTheAmbientTraceSoLogsAndTracesLineUp()
    {
        using var activity = new Activity("test").Start();

        var logger = new ScopeRecordingLogger();
        var scope = new WorkMateLogScope(new ShellSettings { Name = "acme-config" });

        using (scope.Begin(logger, Authenticated("aisha")))
        {
        }

        logger.Scopes.Single().CorrelationId.Should().Be(activity.TraceId.ToString());
    }

    [Fact]
    public async Task TheMiddlewareTakesTheCallersCorrelationIdWhenThereIsOne()
    {
        // A request that crossed a gateway or came from the mobile app keeps the id it arrived
        // with, so one journey is one id end to end.
        var (context, logger) = await RunMiddlewareAsync(configure: http =>
            http.Request.Headers[WorkMateLogScopeMiddleware.CorrelationIdHeader] = "from-the-gateway");

        logger.Scopes.Single().CorrelationId.Should().Be("from-the-gateway");
        context.Response.Headers[WorkMateLogScopeMiddleware.CorrelationIdHeader]
            .ToString().Should().Be("from-the-gateway", "a caller should be able to quote the id back to us");
    }

    [Fact]
    public async Task AnOverlongCorrelationIdFromACallerIsTruncated()
    {
        // The header is untrusted input that ends up on every line for the request.
        var (_, logger) = await RunMiddlewareAsync(configure: http =>
            http.Request.Headers[WorkMateLogScopeMiddleware.CorrelationIdHeader] = new string('x', 500));

        logger.Scopes.Single().CorrelationId.Should().HaveLength(64);
    }

    [Fact]
    public async Task TheResponseCarriesACorrelationIdEvenWhenTheCallerSuppliedNone()
    {
        var (context, _) = await RunMiddlewareAsync();

        context.Response.Headers[WorkMateLogScopeMiddleware.CorrelationIdHeader]
            .ToString().Should().NotBeNullOrWhiteSpace();
    }

    private static async Task<(HttpContext Context, ScopeRecordingLogger Logger)> RunMiddlewareAsync(
        Action<HttpContext>? configure = null)
    {
        var logger = new ScopeRecordingLogger();
        var scope = new WorkMateLogScope(new ShellSettings { Name = "acme-config" });
        var middleware = new WorkMateLogScopeMiddleware(scope, logger);

        var context = new DefaultHttpContext { User = Authenticated("aisha") };
        configure?.Invoke(context);

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        return (context, logger);
    }

    private static ClaimsPrincipal Authenticated(string name) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Name, name)], "test"));

    private sealed class ScopeRecordingLogger : ILogger, ILogger<WorkMateLogScopeMiddleware>
    {
        public List<WorkMateLogScopeState> Scopes { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            if (state is WorkMateLogScopeState values)
            {
                Scopes.Add(values);
            }

            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
