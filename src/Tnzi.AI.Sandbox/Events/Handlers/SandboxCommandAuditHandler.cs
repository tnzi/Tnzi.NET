using System.Text.Json;
using Tnzi.Audit.Entities;
using Tnzi.Audit.Metadata;
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
/// 审计落库是持久化副作用，本处理器不吞异常。<see cref="IAuditSender.SendAsync"/>
/// 失败时让异常冒泡给事件总线（LocalEventBus 对同步与后台处理器统一做错误隔离、
/// LogError、重试与死信队列），不会影响 agent 的 tool-call 成功/失败语义。
/// 此前整个方法体包 log-only try/catch，会架空总线的重试与 DLQ。
/// </para>
/// </remarks>
public class SandboxCommandAuditHandler : IEventHandler<SandboxCommandExecutedEvent>
{
    private const string FunctionName = "AI.Sandbox.CommandExecuted";

    private readonly IAuditSender? _auditSender;

    public SandboxCommandAuditHandler(IAuditSender? auditSender = null)
    {
        _auditSender = auditSender;
    }

    public async Task HandleAsync(SandboxCommandExecutedEvent @event, CancellationToken cancellationToken = default)
    {
        if (_auditSender is null)
        {
            // Audit 模块未加载（可选依赖），无处落库，直接短路。
            return;
        }

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

        // 持久化副作用：发送失败让异常冒泡给总线（隔离/重试/DLQ），不吞。
        await _auditSender.SendAsync(operation);
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
