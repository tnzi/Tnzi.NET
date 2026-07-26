using Tnzi.AI.Sandbox.Events;
using Tnzi.AI.Sandbox.Events.Handlers;
using Tnzi.AI.Sandbox.Tools;
using Tnzi.Audit.Entities;
using Tnzi.Audit.Metadata;
using Tnzi.Audit.Services;
using Tnzi.EventBus;

namespace Tnzi.AI.Tests.Sandbox;

/// <summary>
/// Tests for the sandbox audit pipeline:
/// <see cref="SandboxTools.BashAsync"/> publishes <see cref="SandboxCommandExecutedEvent"/>,
/// and <see cref="SandboxCommandAuditHandler"/> converts the event into an
/// <see cref="AuditOperation"/> via <see cref="IAuditSender"/>.
/// </summary>
public class SandboxCommandAuditTests : IAsyncLifetime
{
    private string _workDir = null!;
    private LocalSandbox _sandbox = null!;
    private VirtualPathTranslator _translator = null!;
    private readonly Guid _threadId = Guid.NewGuid();

    public Task InitializeAsync()
    {
        _workDir = Path.Combine(Path.GetTempPath(), $"tnzi-audit-test-{Guid.NewGuid():N}");
        var threadDir = Path.Combine(_workDir, _threadId.ToString("N"));
        Directory.CreateDirectory(Path.Combine(threadDir, "workspace"));

        _translator = new VirtualPathTranslator(_workDir);
        _sandbox = new LocalSandbox(
            id: "audit-test",
            workspacePath: Path.Combine(threadDir, "workspace"),
            commandTimeout: TimeSpan.FromSeconds(5),
            maxOutputSize: 1024,
            deniedCommands: ["rm -rf /"]);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _sandbox.DisposeAsync();
        if (Directory.Exists(_workDir))
            Directory.Delete(_workDir, recursive: true);
    }

    // ------------------------------------------------------------------ //
    // SandboxTools - event publication
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task BashAsync_NoEventBus_StillSucceeds()
    {
        // EventBus is optional - no observers, no problem.
        var tools = CreateTools(eventBus: null);

        var result = await tools.BashAsync("echo hello");

        result.ShouldNotBeNull();
    }

    [Fact]
    public async Task BashAsync_PublishesExecutedEvent_OnSuccess()
    {
        var bus = new CapturingEventBus();
        var tools = CreateTools(eventBus: bus);

        await tools.BashAsync("echo audit-success");

        bus.Captured.Count.ShouldBe(1);
        var evt = bus.Captured[0].ShouldBeOfType<SandboxCommandExecutedEvent>();
        evt.SandboxId.ShouldBe("audit-test");
        evt.ThreadId.ShouldBe(_threadId);
        evt.Command.ShouldBe("echo audit-success");
        evt.ExitCode.ShouldBe(0);
        evt.Denied.ShouldBeFalse();
        evt.DurationMs.ShouldBeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task BashAsync_PublishesDeniedEvent_OnSubstringMatch()
    {
        var bus = new CapturingEventBus();
        var tools = CreateTools(eventBus: bus);

        await tools.BashAsync("rm -rf /");

        var evt = bus.Captured.Single().ShouldBeOfType<SandboxCommandExecutedEvent>();
        evt.Denied.ShouldBeTrue();
        evt.ExitCode.ShouldBe(-1);
        evt.DenialReason.ShouldNotBeNullOrEmpty();
        evt.DenialReason!.ShouldContain("Command denied");
    }

    [Fact]
    public async Task BashAsync_EventBusThrowing_DoesNotBreakAgent()
    {
        var bus = new ThrowingEventBus();
        var tools = CreateTools(eventBus: bus);

        // Must not throw - observability failures must not abort the agent's tool call.
        var result = await tools.BashAsync("echo resilient");

        result.ShouldNotBeNull();
    }

    // ------------------------------------------------------------------ //
    // SandboxCommandAuditHandler - IAuditSender integration
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task AuditHandler_NoSender_IsNoOp()
    {
        // Audit module not loaded → IAuditSender = null → handler short-circuits.
        var handler = new SandboxCommandAuditHandler(auditSender: null);

        await handler.HandleAsync(NewEvent(exitCode: 0));
        // No assertion needed - passing means no exception raised.
    }

    [Fact]
    public async Task AuditHandler_Success_RecordsSuccessAuditOperation()
    {
        var sender = new CapturingAuditSender();
        var handler = new SandboxCommandAuditHandler(auditSender: sender);

        await handler.HandleAsync(NewEvent(exitCode: 0));

        var op = sender.Captured.Single();
        op.FunctionName.ShouldBe("AI.Sandbox.CommandExecuted");
        op.ResultType.ShouldBe(AuditResultType.Success);
        op.Message.ShouldBe("OK");
        op.RequestParameters.ShouldNotBeNullOrEmpty();
        op.RequestParameters!.ShouldContain("audit-test");
        op.ResponseResult.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task AuditHandler_NonZeroExit_RecordsFailure()
    {
        var sender = new CapturingAuditSender();
        var handler = new SandboxCommandAuditHandler(auditSender: sender);

        await handler.HandleAsync(NewEvent(exitCode: 5));

        sender.Captured.Single().ResultType.ShouldBe(AuditResultType.Failed);
        sender.Captured.Single().Message.ShouldContain("Non-zero exit (5)");
    }

    [Fact]
    public async Task AuditHandler_Denial_RecordsWarning()
    {
        var sender = new CapturingAuditSender();
        var handler = new SandboxCommandAuditHandler(auditSender: sender);

        await handler.HandleAsync(NewEvent(
            exitCode: -1, denied: true, denialReason: "Command denied: contains blocked pattern 'rm -rf /'"));

        var op = sender.Captured.Single();
        op.ResultType.ShouldBe(AuditResultType.Warning);
        op.Message.ShouldContain("Command denied");
    }

    [Fact]
    public async Task AuditHandler_SenderThrows_Propagates()
    {
        var sender = new ThrowingAuditSender();
        var handler = new SandboxCommandAuditHandler(auditSender: sender);

        // 审计落库是持久化副作用：sender 抛异常必须冒泡给事件总线（隔离/重试/DLQ），不再吞。
        await Should.ThrowAsync<InvalidOperationException>(() => handler.HandleAsync(NewEvent(exitCode: 0)));
    }

    // ------------------------------------------------------------------ //
    // Helpers
    // ------------------------------------------------------------------ //

    /// <summary>
    /// 创建 SandboxTools 并在当前测试的异步流上发布沙箱环境
    /// （模拟 SandboxMiddleware 的发布动作 - 必须在测试方法体内调用）。
    /// </summary>
    private SandboxTools CreateTools(IEventBus? eventBus)
    {
        var accessor = new AgentExecutionContextAccessor();
        accessor.Properties[SandboxPropertyKeys.ToolEnvironment] =
            new SandboxToolEnvironment(_sandbox, _threadId);
        return new SandboxTools(_translator, NullLogger<SandboxTools>.Instance, accessor, eventBus: eventBus);
    }

    private SandboxCommandExecutedEvent NewEvent(int exitCode, bool denied = false, string? denialReason = null) => new()
    {
        SandboxId = "audit-test",
        ThreadId = _threadId,
        Command = "echo hi",
        ExitCode = exitCode,
        Output = "hi",
        Stderr = string.Empty,
        DurationMs = 12,
        Denied = denied,
        DenialReason = denialReason,
        ExecutedAt = DateTime.UtcNow
    };

    private sealed class CapturingEventBus : IEventBus
    {
        public List<IEvent> Captured { get; } = [];

        public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : class, IEvent
        {
            Captured.Add(@event);
            return Task.CompletedTask;
        }

        public Task PublishDelayedAsync<TEvent>(TEvent @event, TimeSpan delay, CancellationToken cancellationToken = default) where TEvent : class, IEvent
        {
            Captured.Add(@event);
            return Task.CompletedTask;
        }

        public bool HasHandlers<TEvent>() where TEvent : class, IEvent => false;

        public int GetHandlerCount<TEvent>() where TEvent : class, IEvent => 0;

        public void Subscribe<TEvent, THandler>() where TEvent : class, IEvent where THandler : class, IEventHandler<TEvent> { }
        public void Unsubscribe<TEvent, THandler>() where TEvent : class, IEvent where THandler : class, IEventHandler<TEvent> { }
        public void UnsubscribeAll<TEvent>() where TEvent : class, IEvent { }
    }

    private sealed class ThrowingEventBus : IEventBus
    {
        public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : class, IEvent
            => throw new InvalidOperationException("Simulated event bus failure");
        public Task PublishDelayedAsync<TEvent>(TEvent @event, TimeSpan delay, CancellationToken cancellationToken = default) where TEvent : class, IEvent
            => throw new InvalidOperationException("Simulated event bus failure");
        public bool HasHandlers<TEvent>() where TEvent : class, IEvent => false;
        public int GetHandlerCount<TEvent>() where TEvent : class, IEvent => 0;
        public void Subscribe<TEvent, THandler>() where TEvent : class, IEvent where THandler : class, IEventHandler<TEvent> { }
        public void Unsubscribe<TEvent, THandler>() where TEvent : class, IEvent where THandler : class, IEventHandler<TEvent> { }
        public void UnsubscribeAll<TEvent>() where TEvent : class, IEvent { }
    }

    private sealed class CapturingAuditSender : IAuditSender
    {
        public List<AuditOperation> Captured { get; } = [];

        public Task SendAsync(AuditOperation operation)
        {
            Captured.Add(operation);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingAuditSender : IAuditSender
    {
        public Task SendAsync(AuditOperation operation)
            => throw new InvalidOperationException("Simulated audit failure");
    }
}
