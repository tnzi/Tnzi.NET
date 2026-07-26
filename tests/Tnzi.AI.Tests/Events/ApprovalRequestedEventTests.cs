namespace Tnzi.AI.Tests.Events;

/// <summary>
/// ApprovalRequestedEvent 发布行为测试
/// </summary>
public class ApprovalRequestedEventTests
{
    private static ApprovalToolWrapper CreateWrapper(
        AIFunction innerFunction,
        IToolApprovalHandler? approvalHandler,
        IToolPermissionEvaluator permissionEvaluator,
        IEventBus? eventBus = null,
        IAgentExecutionContextAccessor? executionContextAccessor = null)
    {
        return new ApprovalToolWrapper(
            innerFunction,
            approvalHandler,
            new ToolApprovalOptions(),
            permissionEvaluator,
            executionContextAccessor: executionContextAccessor,
            eventBus: eventBus);
    }

    [Fact]
    public async Task InvokeAsync_DenyDecision_PublishesApprovalRequestedEvent_WithDecisionDeny()
    {
        var inner = AIFunctionFactory.Create(() => "should-not-run", "dangerous_tool", "Dangerous tool");
        var evaluator = new ToolPermissionEvaluator(
        [
            new ToolPermissionRule
            {
                ToolPattern = "dangerous_tool",
                Behavior = PermissionBehavior.Deny,
                Reason = "Blocked by policy"
            }
        ]);

        ApprovalRequestedEvent? capturedEvent = null;
        var eventBus = new Mock<IEventBus>();
        eventBus
            .Setup(x => x.PublishAsync(It.IsAny<ApprovalRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<ApprovalRequestedEvent, CancellationToken>((e, _) => capturedEvent = e)
            .Returns(Task.CompletedTask);

        var wrapper = CreateWrapper(inner, approvalHandler: null, evaluator, eventBus.Object);

        var result = await wrapper.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>()));

        result.ShouldBe("Tool call rejected: Blocked by policy");

        eventBus.Verify(
            x => x.PublishAsync(It.IsAny<ApprovalRequestedEvent>(), It.IsAny<CancellationToken>()),
            Times.Once);

        capturedEvent.ShouldNotBeNull();
        capturedEvent.ToolName.ShouldBe("dangerous_tool");
        capturedEvent.Decision.ShouldBe("Deny");
        capturedEvent.Reason.ShouldBe("Blocked by policy");
    }

    [Fact]
    public async Task InvokeAsync_AskDecision_PublishesApprovalRequestedEvent_WithDecisionAsk()
    {
        var inner = AIFunctionFactory.Create(() => "approved-result", "shell_exec", "Shell execution");
        var evaluator = new ToolPermissionEvaluator(
        [
            new ToolPermissionRule
            {
                ToolPattern = "shell_exec",
                Behavior = PermissionBehavior.Ask,
                Reason = "Approval required"
            }
        ]);

        var approvalHandler = new Mock<IToolApprovalHandler>();
        approvalHandler
            .Setup(x => x.RequestApprovalAsync(It.IsAny<ToolApprovalRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ToolApprovalResult.Approve("tester"));

        ApprovalRequestedEvent? capturedEvent = null;
        var eventBus = new Mock<IEventBus>();
        eventBus
            .Setup(x => x.PublishAsync(It.IsAny<ApprovalRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<ApprovalRequestedEvent, CancellationToken>((e, _) => capturedEvent = e)
            .Returns(Task.CompletedTask);

        var wrapper = CreateWrapper(inner, approvalHandler.Object, evaluator, eventBus.Object);

        await wrapper.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>()));

        eventBus.Verify(
            x => x.PublishAsync(It.IsAny<ApprovalRequestedEvent>(), It.IsAny<CancellationToken>()),
            Times.Once);

        capturedEvent.ShouldNotBeNull();
        capturedEvent.ToolName.ShouldBe("shell_exec");
        capturedEvent.Decision.ShouldBe("Ask");
        capturedEvent.Reason.ShouldBe("Approval required");
    }

    [Fact]
    public async Task InvokeAsync_AskDecision_PublishesEvent_BeforeCallingApprovalHandler()
    {
        var inner = AIFunctionFactory.Create(() => "approved-result", "shell_exec", "Shell execution");
        var evaluator = new ToolPermissionEvaluator(
        [
            new ToolPermissionRule
            {
                ToolPattern = "shell_exec",
                Behavior = PermissionBehavior.Ask
            }
        ]);

        var callOrder = new List<string>();

        var approvalHandler = new Mock<IToolApprovalHandler>();
        approvalHandler
            .Setup(x => x.RequestApprovalAsync(It.IsAny<ToolApprovalRequest>(), It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("approval"))
            .ReturnsAsync(ToolApprovalResult.Approve("tester"));

        var eventBus = new Mock<IEventBus>();
        eventBus
            .Setup(x => x.PublishAsync(It.IsAny<ApprovalRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<ApprovalRequestedEvent, CancellationToken>((_, _) => callOrder.Add("event"))
            .Returns(Task.CompletedTask);

        var wrapper = CreateWrapper(inner, approvalHandler.Object, evaluator, eventBus.Object);

        await wrapper.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>()));

        callOrder.ShouldBe(["event", "approval"]);
    }

    [Fact]
    public async Task InvokeAsync_DenyDecision_EventPublishingFailure_DoesNotAffectRejection()
    {
        var inner = AIFunctionFactory.Create(() => "should-not-run", "dangerous_tool", "Dangerous tool");
        var evaluator = new ToolPermissionEvaluator(
        [
            new ToolPermissionRule
            {
                ToolPattern = "dangerous_tool",
                Behavior = PermissionBehavior.Deny,
                Reason = "Blocked"
            }
        ]);

        var eventBus = new Mock<IEventBus>();
        eventBus
            .Setup(x => x.PublishAsync(It.IsAny<ApprovalRequestedEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Event bus unavailable"));

        var wrapper = CreateWrapper(inner, approvalHandler: null, evaluator, eventBus.Object);

        var result = await wrapper.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>()));

        // Rejection message is still returned despite event bus failure
        result.ShouldBe("Tool call rejected: Blocked");
    }

    [Fact]
    public async Task InvokeAsync_AskDecision_EventPublishingFailure_DoesNotAffectToolExecution()
    {
        var inner = AIFunctionFactory.Create(() => "approved-result", "shell_exec", "Shell execution");
        var evaluator = new ToolPermissionEvaluator(
        [
            new ToolPermissionRule
            {
                ToolPattern = "shell_exec",
                Behavior = PermissionBehavior.Ask
            }
        ]);

        var approvalHandler = new Mock<IToolApprovalHandler>();
        approvalHandler
            .Setup(x => x.RequestApprovalAsync(It.IsAny<ToolApprovalRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ToolApprovalResult.Approve("tester"));

        var eventBus = new Mock<IEventBus>();
        eventBus
            .Setup(x => x.PublishAsync(It.IsAny<ApprovalRequestedEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Event bus unavailable"));

        var wrapper = CreateWrapper(inner, approvalHandler.Object, evaluator, eventBus.Object);

        var result = await wrapper.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>()));

        var text = result switch
        {
            JsonElement json => json.GetString(),
            string value => value,
            _ => result?.ToString()
        };

        // Tool still executes despite event bus failure
        text.ShouldBe("approved-result");
    }

    [Fact]
    public async Task InvokeAsync_DenyDecision_WithRunId_PublishesEventWithCorrectRunId()
    {
        var runId = Guid.NewGuid();
        var inner = AIFunctionFactory.Create(() => "should-not-run", "dangerous_tool", "Dangerous tool");
        var evaluator = new ToolPermissionEvaluator(
        [
            new ToolPermissionRule
            {
                ToolPattern = "dangerous_tool",
                Behavior = PermissionBehavior.Deny,
                Reason = "Blocked"
            }
        ]);

        ApprovalRequestedEvent? capturedEvent = null;
        var eventBus = new Mock<IEventBus>();
        eventBus
            .Setup(x => x.PublishAsync(It.IsAny<ApprovalRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<ApprovalRequestedEvent, CancellationToken>((e, _) => capturedEvent = e)
            .Returns(Task.CompletedTask);

        var accessor = new AgentExecutionContextAccessor();
        accessor.Properties[ContextPropertyKeys.CurrentRunId] = runId;

        var wrapper = CreateWrapper(inner, approvalHandler: null, evaluator, eventBus.Object, accessor);

        await wrapper.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>()));

        capturedEvent.ShouldNotBeNull();
        capturedEvent.RunId.ShouldBe(runId);
    }

    [Fact]
    public async Task InvokeAsync_DenyDecision_WithUserId_PublishesEventWithCorrectUserId()
    {
        var userId = Guid.NewGuid();
        var inner = AIFunctionFactory.Create(() => "should-not-run", "dangerous_tool", "Dangerous tool");
        var evaluator = new ToolPermissionEvaluator(
        [
            new ToolPermissionRule
            {
                ToolPattern = "dangerous_tool",
                Behavior = PermissionBehavior.Deny,
                Reason = "Blocked"
            }
        ]);

        ApprovalRequestedEvent? capturedEvent = null;
        var eventBus = new Mock<IEventBus>();
        eventBus
            .Setup(x => x.PublishAsync(It.IsAny<ApprovalRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<ApprovalRequestedEvent, CancellationToken>((e, _) => capturedEvent = e)
            .Returns(Task.CompletedTask);

        var accessor = new AgentExecutionContextAccessor
        {
            CurrentRequest = new AgentRunRequest { UserId = userId }
        };

        var wrapper = CreateWrapper(inner, approvalHandler: null, evaluator, eventBus.Object, accessor);

        await wrapper.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>()));

        capturedEvent.ShouldNotBeNull();
        capturedEvent.UserId.ShouldBe(userId);
    }

    [Fact]
    public async Task InvokeAsync_DenyDecision_WithoutEventBus_DoesNotThrow()
    {
        var inner = AIFunctionFactory.Create(() => "should-not-run", "dangerous_tool", "Dangerous tool");
        var evaluator = new ToolPermissionEvaluator(
        [
            new ToolPermissionRule
            {
                ToolPattern = "dangerous_tool",
                Behavior = PermissionBehavior.Deny,
                Reason = "Blocked"
            }
        ]);

        // No event bus passed - should complete without error
        var wrapper = CreateWrapper(inner, approvalHandler: null, evaluator, eventBus: null);

        var result = await wrapper.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>()));

        result.ShouldBe("Tool call rejected: Blocked");
    }

    [Fact]
    public async Task InvokeAsync_AllowDecision_DoesNotPublishApprovalEvent()
    {
        var inner = AIFunctionFactory.Create(() => "allowed-result", "safe_tool", "Safe tool");
        var evaluator = new ToolPermissionEvaluator(
        [
            new ToolPermissionRule
            {
                ToolPattern = "safe_tool",
                Behavior = PermissionBehavior.Allow
            }
        ]);

        var eventBus = new Mock<IEventBus>();

        var wrapper = CreateWrapper(inner, approvalHandler: null, evaluator, eventBus.Object);

        await wrapper.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>()));

        eventBus.Verify(
            x => x.PublishAsync(It.IsAny<ApprovalRequestedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task InvokeAsync_AllowDecision_PublishesToolCallExecutedEvent_WithSuccess()
    {
        var inner = AIFunctionFactory.Create(() => "allowed-result", "safe_tool", "Safe tool");
        var evaluator = new ToolPermissionEvaluator(
        [
            new ToolPermissionRule
            {
                ToolPattern = "safe_tool",
                Behavior = PermissionBehavior.Allow
            }
        ]);

        ToolCallExecutedEvent? capturedEvent = null;
        var eventBus = new Mock<IEventBus>();
        eventBus
            .Setup(x => x.PublishAsync(It.IsAny<ToolCallExecutedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<ToolCallExecutedEvent, CancellationToken>((e, _) => capturedEvent = e)
            .Returns(Task.CompletedTask);

        var wrapper = CreateWrapper(inner, approvalHandler: null, evaluator, eventBus.Object);

        await wrapper.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>()));

        eventBus.Verify(
            x => x.PublishAsync(It.IsAny<ToolCallExecutedEvent>(), It.IsAny<CancellationToken>()),
            Times.Once);

        capturedEvent.ShouldNotBeNull();
        capturedEvent.ToolName.ShouldBe("safe_tool");
        capturedEvent.Success.ShouldBeTrue();
        capturedEvent.DurationMs.ShouldBeGreaterThanOrEqualTo(0);
        capturedEvent.ErrorMessage.ShouldBeNull();
    }

    [Fact]
    public async Task InvokeAsync_ToolThrows_PublishesToolCallExecutedEvent_WithFailure()
    {
        var inner = AIFunctionFactory.Create(
            (Func<string>)(() => throw new InvalidOperationException("Tool exploded")),
            "failing_tool", "Failing tool");

        var evaluator = new ToolPermissionEvaluator(
        [
            new ToolPermissionRule
            {
                ToolPattern = "failing_tool",
                Behavior = PermissionBehavior.Allow
            }
        ]);

        ToolCallExecutedEvent? capturedEvent = null;
        var eventBus = new Mock<IEventBus>();
        eventBus
            .Setup(x => x.PublishAsync(It.IsAny<ToolCallExecutedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<ToolCallExecutedEvent, CancellationToken>((e, _) => capturedEvent = e)
            .Returns(Task.CompletedTask);

        var wrapper = CreateWrapper(inner, approvalHandler: null, evaluator, eventBus.Object);

        await Should.ThrowAsync<InvalidOperationException>(
            () => wrapper.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>())).AsTask());

        eventBus.Verify(
            x => x.PublishAsync(It.IsAny<ToolCallExecutedEvent>(), It.IsAny<CancellationToken>()),
            Times.Once);

        capturedEvent.ShouldNotBeNull();
        capturedEvent.ToolName.ShouldBe("failing_tool");
        capturedEvent.Success.ShouldBeFalse();
        capturedEvent.ErrorMessage.ShouldBe("Tool exploded");
        capturedEvent.DurationMs.ShouldBeGreaterThanOrEqualTo(0);
    }
}
