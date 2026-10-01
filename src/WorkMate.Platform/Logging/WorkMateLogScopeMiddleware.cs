using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using OrchardCore.Environment.Shell;

namespace WorkMate.Platform.Logging;

/// <summary>
/// Puts tenant, user and correlation id on every log line, as specification section 9 requires.
/// </summary>
/// <remarks>
/// A logging scope rather than a layout renderer, deliberately. Orchard Core 3.0.1's only logging
/// package is OrchardCore.Logging.NLog, whose TenantLayoutRenderer gives the tenant and nothing
/// else, and ties the solution to NLog and an NLog.config. A scope is plain
/// Microsoft.Extensions.Logging: it reaches every line written by any code inside the request,
/// including Orchard's own, and works with whichever sink a deployment configures.
///
/// The correlation id is taken from the caller's header when there is one, so a request that
/// crossed a gateway or came from the mobile app keeps the id it arrived with. Otherwise it is
/// the current Activity's trace id, which is what distributed tracing already uses, so logs and
/// traces line up without a second identifier.
///
/// Background jobs have no request and so do not pass through here. Section 9 requires them to
/// log with tenant context too; they use <see cref="IWorkMateLogScope"/> directly.
/// </remarks>
public sealed class WorkMateLogScopeMiddleware : IMiddleware
{
    /// <summary>The header a caller may use to supply its own correlation id.</summary>
    public const string CorrelationIdHeader = "X-Correlation-Id";

    private readonly IWorkMateLogScope _logScope;
    private readonly ILogger<WorkMateLogScopeMiddleware> _logger;

    public WorkMateLogScopeMiddleware(
        IWorkMateLogScope logScope,
        ILogger<WorkMateLogScopeMiddleware> logger)
    {
        _logScope = logScope;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var correlationId = ResolveCorrelationId(context);

        // Echo it back so that a caller can quote the id when reporting a problem.
        context.Response.Headers[CorrelationIdHeader] = correlationId;

        using (_logScope.Begin(_logger, context.User, correlationId))
        {
            await next(context);
        }
    }

    private static string ResolveCorrelationId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(CorrelationIdHeader, out var supplied))
        {
            var value = supplied.ToString();

            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Length > MaximumCorrelationIdLength
                    ? value[..MaximumCorrelationIdLength]
                    : value;
            }
        }

        return Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
    }

    /// <summary>
    /// A caller-supplied header is untrusted input that ends up in every log line for the
    /// request, so its length is capped.
    /// </summary>
    private const int MaximumCorrelationIdLength = 64;
}

/// <summary>
/// Begins the logging scope that carries tenant, user and correlation id. Background jobs, which
/// have no request to hang a middleware off, use this directly.
/// </summary>
public interface IWorkMateLogScope
{
    /// <summary>
    /// Begins a scope on <paramref name="logger"/>. Disposing it ends the scope.
    /// </summary>
    /// <param name="logger">Any logger; the scope applies to every logger in the same context.</param>
    /// <param name="user">The acting user, or null for work that has none.</param>
    /// <param name="correlationId">The id tying these lines together, or null to generate one.</param>
    IDisposable Begin(ILogger logger, ClaimsPrincipal? user, string? correlationId = null);
}

/// <inheritdoc />
public sealed class WorkMateLogScope : IWorkMateLogScope
{
    private readonly ShellSettings _shellSettings;

    public WorkMateLogScope(ShellSettings shellSettings) => _shellSettings = shellSettings;

    public IDisposable Begin(ILogger logger, ClaimsPrincipal? user, string? correlationId = null)
    {
        ArgumentNullException.ThrowIfNull(logger);

        var state = new WorkMateLogScopeState(
            _shellSettings.Name,
            NameOf(user),
            correlationId ?? Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("n"));

        return logger.BeginScope(state) ?? NullScope.Instance;
    }

    /// <summary>
    /// Anonymous rather than empty: a log line that says nothing about the user is
    /// indistinguishable from one where the field was forgotten.
    /// </summary>
    private static string NameOf(ClaimsPrincipal? user) =>
        user?.Identity?.IsAuthenticated == true
            ? user.Identity.Name ?? "(unnamed)"
            : "(anonymous)";

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}

/// <summary>
/// The scope's state: tenant, user and correlation id.
/// </summary>
/// <remarks>
/// A list of key/value pairs with a readable <see cref="ToString"/>, rather than a dictionary.
/// This is the shape every logging provider expects, and the difference is not cosmetic. A plain
/// dictionary passed to BeginScope satisfies the compiler and then prints as
/// "System.Collections.Generic.Dictionary`2[System.String,System.Object]" in the console and
/// reaches a structured sink as one opaque object — so the tenant, user and correlation id
/// section 9 asks for are technically in the scope and useless in practice. Implementing
/// IReadOnlyList&lt;KeyValuePair&lt;string, object&gt;&gt; is what makes them readable in a
/// console and queryable in a structured sink.
/// </remarks>
public sealed class WorkMateLogScopeState : IReadOnlyList<KeyValuePair<string, object>>
{
    private readonly KeyValuePair<string, object>[] _values;
    private readonly string _formatted;

    public WorkMateLogScopeState(string tenant, string user, string correlationId)
    {
        Tenant = tenant;
        User = user;
        CorrelationId = correlationId;

        _values =
        [
            new(nameof(Tenant), tenant),
            new(nameof(User), user),
            new(nameof(CorrelationId), correlationId),
        ];

        _formatted = $"Tenant:{tenant} User:{user} CorrelationId:{correlationId}";
    }

    public string Tenant { get; }

    public string User { get; }

    public string CorrelationId { get; }

    public KeyValuePair<string, object> this[int index] => _values[index];

    public int Count => _values.Length;

    public IEnumerator<KeyValuePair<string, object>> GetEnumerator() =>
        ((IEnumerable<KeyValuePair<string, object>>)_values).GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _values.GetEnumerator();

    public override string ToString() => _formatted;
}
