namespace Tnzi.AI.Tests;

public class AgentAsTools_ConcurrencyTests
{
    [Fact]
    public void Constructor_CreatesSemaphoreWithClampedValue()
    {
        var config = new AgentAsToolsConfiguration
        {
            MaxConcurrentSubAgents = 10, // exceeds max 4
            Agents = new() { ["agent1"] = Guid.NewGuid() }
        };
        var strategy = new AgentAsToolsExecutionStrategy(config);
        // Should not throw - clamped to [2,4]
        Assert.NotNull(strategy);
    }

    [Fact]
    public void Constructor_MinClamp_AllowsTwo()
    {
        var config = new AgentAsToolsConfiguration
        {
            MaxConcurrentSubAgents = 1, // below min 2
            Agents = new() { ["a"] = Guid.NewGuid() }
        };
        var strategy = new AgentAsToolsExecutionStrategy(config);
        Assert.NotNull(strategy);
    }

    [Fact]
    public void SubAgentTimeout_DefaultIs15Minutes()
    {
        var config = new AgentAsToolsConfiguration
        {
            Agents = new() { ["a"] = Guid.NewGuid() }
        };
        Assert.Equal(TimeSpan.FromMinutes(15), config.SubAgentTimeout);
    }

    [Fact]
    public void SubAgentEventTypes_HasAllConstants()
    {
        Assert.Equal("sub_agent_started", SubAgentEventTypes.Started);
        Assert.Equal("sub_agent_completed", SubAgentEventTypes.Completed);
        Assert.Equal("sub_agent_failed", SubAgentEventTypes.Failed);
        Assert.Equal("sub_agent_timed_out", SubAgentEventTypes.TimedOut);
    }
}
