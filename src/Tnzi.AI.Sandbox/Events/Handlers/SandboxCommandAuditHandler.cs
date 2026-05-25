using System.Text.Json;
using Tnzi.Audit.Entities;
using Tnzi.Audit.Services;

namespace Tnzi.AI.Sandbox.Events.Handlers;

/// <summary>
/// Persists <see cref="SandboxCommandExecutedEvent"/> as an
/// <see cref="AuditOperation"/> via <see cref="IAuditSender"/> (the audit
/// module's async background channel).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IAuditSender"/> is injected as <c>optional</c>: if the
/// <c>Tnzi.Audit</c> module is not loaded into the application graph the
/// service is absent from DI, the handler short-circuits, and the sandbox
/// continues to operate normally. This keeps audit a soft dependency.
/// </para>
/// <para>
/// The handler MUST never let an exception propagate — failure to audit must
/// not affect the agent's tool-call success/failure semantics.
/// </para>
/// </remarks>
public class SandboxCommandAuditHandler : IEventHandler<SandboxCommandExecutedEvent>
{
    private const string FunctionName = "AI.Sandbox.CommandExecuted";

    private readonly IAuditSender? _auditSender;
    private readonly ILogger<SandboxCommandAuditHandler> _logger;

    public SandboxCommandAuditHandler(
        ILogger<SandboxCommandAuditHandler> logger,
        IAuditSender? auditSender = null)
    {
        _logger = Check.NotNull(logger);
        _auditSender = auditSender;
    }

    public async Task HandleAsync(SandboxCommandExecutedEvent @event, CancellationToken cancellationToken = default)
    {
        if (_auditSender is null)
        {
            // Audit module not present in this application — nothing to do.
            return;
        }

        try
        {
            var operation = new AuditOperation
            {
                FunctionName = FunctionName,
                UserId = @event.UserId,
                TenantId = @event.TenantId,
                ResultType = ResolveResultType(@event),
                Message = ResolveMessage(@event),
                Elapsed = @event.DurationMs,
                RequestParameters = JsonSerializer.Serialize(new
                {
                    sandboxId = @event.SandboxId,
                    threadId = @event.ThreadId,
                    command = @event.Command
                }),
                ResponseResult = JsonSerializer.Serialize(new
                {
                    exitCode = @event.ExitCode,
                    stdout = @event.Output,
                    stderr = @event.Stderr,
                    denied = @event.Denied,
                    denialReason = @event.DenialReason
                }),
                StartTime = @event.ExecutedAt,
                EndTime = @event.ExecutedAt.AddMilliseconds(@event.DurationMs)
            };

            await _auditSender.SendAsync(operation);
        }
        catch (Exception ex)
        {
            // Silent catch — audit failure must not break sandbox tool calls.
            _logger.LogWarning(ex, "Failed to audit sandbox command for sandbox {SandboxId}", @event.SandboxId);
        }
    }

    private static AuditResultType ResolveResultType(SandboxCommandExecutedEvent @event)
    {
        if (@event.Denied)
            return AuditResultType.Warning;
        return @event.ExitCode == 0 ? AuditResultType.Success : AuditResultType.Failed;
    }

    private static string ResolveMessage(SandboxCommandExecutedEvent @event)
    {
        if (@event.Denied)
            return @event.DenialReason ?? "Command denied by sandbox policy";
        return @event.ExitCode == 0 ? "OK" : $"Non-zero exit ({@event.ExitCode})";
    }
}
