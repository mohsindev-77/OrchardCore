using Microsoft.Extensions.Logging;
using OrchardCore.Environment.Shell;

namespace WorkMate.Platform.Services;

/// <inheritdoc />
public sealed partial class SystemOperation : ISystemOperation
{
    // Operator-facing, not user-facing, so not localised. Information rather than Debug: "what
    // ran as the system" is an audit question, and an answer that is off by default is no answer.
    [LoggerMessage(
        EventId = 1100,
        Level = LogLevel.Information,
        Message = "Tenant '{TenantName}' entered system authority: {Reason}.")]
    private partial void LogEntered(string tenantName, string reason);

    [LoggerMessage(
        EventId = 1101,
        Level = LogLevel.Warning,
        Message = "Tenant '{TenantName}' left system authority for '{Reason}' out of order. "
            + "A nested scope was disposed after the one that contained it, which means a using "
            + "statement is missing or a handle escaped the method that created it.")]
    private partial void LogDisposedOutOfOrder(string tenantName, string reason);

    private readonly ShellSettings _shellSettings;
    private readonly ILogger<SystemOperation> _logger;

    /// <summary>
    /// The open handles, innermost last. A list rather than a counter so that disposing out of
    /// order is detectable rather than silently miscounting.
    /// </summary>
    private readonly List<Handle> _open = [];

    public SystemOperation(ShellSettings shellSettings, ILogger<SystemOperation> logger)
    {
        _shellSettings = shellSettings;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsActive => _open.Count > 0;

    /// <inheritdoc />
    public IDisposable Begin(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        var handle = new Handle(this, reason);
        _open.Add(handle);

        LogEntered(_shellSettings.Name, reason);

        return handle;
    }

    private void End(Handle handle)
    {
        var index = _open.LastIndexOf(handle);

        if (index < 0)
        {
            // Already disposed. Disposing twice is harmless and a using statement inside a
            // try/finally can do it, so this is not worth a warning.
            return;
        }

        if (index != _open.Count - 1)
        {
            LogDisposedOutOfOrder(_shellSettings.Name, handle.Reason);
        }

        // Everything opened inside this handle ends with it. Leaving an inner handle open after
        // its container closed would hold authority for code that never asked for it.
        _open.RemoveRange(index, _open.Count - index);
    }

    private sealed class Handle : IDisposable
    {
        private readonly SystemOperation _owner;

        public Handle(SystemOperation owner, string reason)
        {
            _owner = owner;
            Reason = reason;
        }

        public string Reason { get; }

        public void Dispose() => _owner.End(this);
    }
}
