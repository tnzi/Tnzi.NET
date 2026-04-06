namespace Tnzi.AI.Tests.Events;

/// <summary>
/// 细粒度运行时事件属性赋值测试
/// </summary>
public class RuntimeEventTests
{
    [Fact]
    public void AgentRunStartedEvent_Properties_AssignedCorrectly()
    {
        var runId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var threadId = Guid.NewGuid();

        var evt = new AgentRunStartedEvent
        {
            RunId = runId,
            AgentId = agentId,
            UserId = userId,
            ThreadId = threadId,
            Provider = "openai",
            Model = "gpt-4o",
            IsStreaming = true,
            ExecutionMode = "Single"
        };

        evt.RunId.ShouldBe(runId);
        evt.AgentId.ShouldBe(agentId);
        evt.UserId.ShouldBe(userId);
        evt.ThreadId.ShouldBe(threadId);
        evt.Provider.ShouldBe("openai");
        evt.Model.ShouldBe("gpt-4o");
        evt.IsStreaming.ShouldBeTrue();
        evt.ExecutionMode.ShouldBe("Single");
    }

    [Fact]
    public void AgentRunStartedEvent_Defaults()
    {
        var evt = new AgentRunStartedEvent();

        evt.RunId.ShouldBeNull();
        evt.AgentId.ShouldBeNull();
        evt.UserId.ShouldBeNull();
        evt.ThreadId.ShouldBeNull();
        evt.Provider.ShouldBeNull();
        evt.Model.ShouldBeNull();
        evt.IsStreaming.ShouldBeFalse();
        evt.ExecutionMode.ShouldBe(string.Empty);
        evt.EventId.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public void AgentRunStartedEvent_InheritsEventBase()
    {
        var evt = new AgentRunStartedEvent();
        evt.ShouldBeAssignableTo<Tnzi.EventBus.EventBase>();
        evt.EventTime.ShouldNotBe(default);
    }

    [Fact]
    public void ToolCallExecutedEvent_Properties_AssignedCorrectly()
    {
        var runId = Guid.NewGuid();
        var threadId = Guid.NewGuid();

        var evt = new ToolCallExecutedEvent
        {
            RunId = runId,
            ThreadId = threadId,
            ToolName = "web_search",
            ToolGroup = "search-tools",
            DurationMs = 350,
            Success = true,
            ErrorMessage = null
        };

        evt.RunId.ShouldBe(runId);
        evt.ThreadId.ShouldBe(threadId);
        evt.ToolName.ShouldBe("web_search");
        evt.ToolGroup.ShouldBe("search-tools");
        evt.DurationMs.ShouldBe(350L);
        evt.Success.ShouldBeTrue();
        evt.ErrorMessage.ShouldBeNull();
    }

    [Fact]
    public void ToolCallExecutedEvent_FailureScenario()
    {
        var evt = new ToolCallExecutedEvent
        {
            ToolName = "bash",
            Success = false,
            ErrorMessage = "Command timed out",
            DurationMs = 30000
        };

        evt.Success.ShouldBeFalse();
        evt.ErrorMessage.ShouldBe("Command timed out");
        evt.DurationMs.ShouldBe(30000L);
    }

    [Fact]
    public void ToolCallExecutedEvent_Defaults()
    {
        var evt = new ToolCallExecutedEvent();

        evt.RunId.ShouldBeNull();
        evt.ThreadId.ShouldBeNull();
        evt.ToolName.ShouldBe(string.Empty);
        evt.ToolGroup.ShouldBeNull();
        evt.DurationMs.ShouldBe(0L);
        evt.Success.ShouldBeFalse();
        evt.ErrorMessage.ShouldBeNull();
    }

    [Fact]
    public void ToolCallExecutedEvent_InheritsEventBase()
    {
        var evt = new ToolCallExecutedEvent();
        evt.ShouldBeAssignableTo<Tnzi.EventBus.EventBase>();
    }

    [Fact]
    public void SubAgentSpawnedEvent_Properties_AssignedCorrectly()
    {
        var parentRunId = Guid.NewGuid();
        var childRunId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var threadId = Guid.NewGuid();

        var evt = new SubAgentSpawnedEvent
        {
            ParentRunId = parentRunId,
            ChildRunId = childRunId,
            SubAgentType = "researcher",
            AgentId = agentId,
            ThreadId = threadId
        };

        evt.ParentRunId.ShouldBe(parentRunId);
        evt.ChildRunId.ShouldBe(childRunId);
        evt.SubAgentType.ShouldBe("researcher");
        evt.AgentId.ShouldBe(agentId);
        evt.ThreadId.ShouldBe(threadId);
    }

    [Fact]
    public void SubAgentSpawnedEvent_Defaults()
    {
        var evt = new SubAgentSpawnedEvent();

        evt.ParentRunId.ShouldBeNull();
        evt.ChildRunId.ShouldBe(Guid.Empty);
        evt.SubAgentType.ShouldBeNull();
        evt.AgentId.ShouldBeNull();
        evt.ThreadId.ShouldBeNull();
    }

    [Fact]
    public void SubAgentSpawnedEvent_InheritsEventBase()
    {
        var evt = new SubAgentSpawnedEvent { ChildRunId = Guid.NewGuid() };
        evt.ShouldBeAssignableTo<Tnzi.EventBus.EventBase>();
        evt.EventId.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public void ApprovalRequestedEvent_Properties_AssignedCorrectly()
    {
        var runId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var evt = new ApprovalRequestedEvent
        {
            RunId = runId,
            ToolName = "bash",
            Decision = "Ask",
            Reason = "Destructive command detected",
            UserId = userId
        };

        evt.RunId.ShouldBe(runId);
        evt.ToolName.ShouldBe("bash");
        evt.Decision.ShouldBe("Ask");
        evt.Reason.ShouldBe("Destructive command detected");
        evt.UserId.ShouldBe(userId);
    }

    [Fact]
    public void ApprovalRequestedEvent_Defaults()
    {
        var evt = new ApprovalRequestedEvent();

        evt.RunId.ShouldBeNull();
        evt.ToolName.ShouldBe(string.Empty);
        evt.Decision.ShouldBe(string.Empty);
        evt.Reason.ShouldBeNull();
        evt.UserId.ShouldBeNull();
    }

    [Fact]
    public void ApprovalRequestedEvent_InheritsEventBase()
    {
        var evt = new ApprovalRequestedEvent();
        evt.ShouldBeAssignableTo<Tnzi.EventBus.EventBase>();
        evt.EventTime.ShouldNotBe(default);
    }

    [Fact]
    public void AgentRunFailedEvent_Properties_AssignedCorrectly()
    {
        var runId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var threadId = Guid.NewGuid();

        var evt = new AgentRunFailedEvent
        {
            RunId = runId,
            AgentId = agentId,
            UserId = userId,
            ThreadId = threadId,
            ErrorMessage = "Provider returned 500",
            ExceptionType = "HttpRequestException",
            DurationMs = 1500,
            IsStreaming = false
        };

        evt.RunId.ShouldBe(runId);
        evt.AgentId.ShouldBe(agentId);
        evt.UserId.ShouldBe(userId);
        evt.ThreadId.ShouldBe(threadId);
        evt.ErrorMessage.ShouldBe("Provider returned 500");
        evt.ExceptionType.ShouldBe("HttpRequestException");
        evt.DurationMs.ShouldBe(1500L);
        evt.IsStreaming.ShouldBeFalse();
    }

    [Fact]
    public void AgentRunFailedEvent_Defaults()
    {
        var evt = new AgentRunFailedEvent();

        evt.RunId.ShouldBeNull();
        evt.AgentId.ShouldBeNull();
        evt.UserId.ShouldBeNull();
        evt.ThreadId.ShouldBeNull();
        evt.ErrorMessage.ShouldBe(string.Empty);
        evt.ExceptionType.ShouldBeNull();
        evt.DurationMs.ShouldBe(0L);
        evt.IsStreaming.ShouldBeFalse();
    }

    [Fact]
    public void AgentRunFailedEvent_InheritsEventBase()
    {
        var evt = new AgentRunFailedEvent();
        evt.ShouldBeAssignableTo<Tnzi.EventBus.EventBase>();
        evt.EventId.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public void AgentRunFailedEvent_StreamingScenario()
    {
        var evt = new AgentRunFailedEvent
        {
            ErrorMessage = "Stream interrupted",
            IsStreaming = true,
            DurationMs = 800
        };

        evt.IsStreaming.ShouldBeTrue();
        evt.ErrorMessage.ShouldBe("Stream interrupted");
        evt.DurationMs.ShouldBe(800L);
    }
}
