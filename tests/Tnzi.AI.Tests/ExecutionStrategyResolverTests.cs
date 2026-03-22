
namespace Tnzi.AI.Tests;

/// <summary>
/// ExecutionStrategyResolver 单元测试 — 验证 Resolve(AgentExecutionMode, string?) 方法
/// </summary>
public class ExecutionStrategyResolverTests
{
    [Fact]
    public void Resolve_SingleMode_ReturnsSingleAgentStrategy()
    {
        var strategy = ExecutionStrategyResolver.Resolve(AgentExecutionMode.Single, null);
        strategy.ShouldBeOfType<SingleAgentStrategy>();
        strategy.ShouldBe(SingleAgentStrategy.Instance);
    }

    [Fact]
    public void Resolve_SingleModeWithConfig_ReturnsSingleAgentStrategy()
    {
        var strategy = ExecutionStrategyResolver.Resolve(AgentExecutionMode.Single, """{"someKey":"value"}""");
        strategy.ShouldBeOfType<SingleAgentStrategy>();
    }

    [Fact]
    public void Resolve_UnknownModeValue_ThrowsInvalidOperation()
    {
        Should.Throw<InvalidOperationException>(
            () => ExecutionStrategyResolver.Resolve((AgentExecutionMode)999, null));
    }

    [Fact]
    public void Resolve_HandoffMode_ReturnsHandoffExecutionStrategy()
    {
        var agentId = Guid.NewGuid();
        var json = $$"""
        {
            "handoff": {
                "targets": {
                    "billing": "{{agentId}}"
                },
                "maxHandoffs": 5
            }
        }
        """;

        var strategy = ExecutionStrategyResolver.Resolve(AgentExecutionMode.Handoff, json);
        strategy.ShouldBeOfType<HandoffExecutionStrategy>();
    }

    [Fact]
    public void Resolve_HandoffModeWithoutConfig_ReturnsHandoffWithDefaults()
    {
        var strategy = ExecutionStrategyResolver.Resolve(AgentExecutionMode.Handoff, null);
        strategy.ShouldBeOfType<HandoffExecutionStrategy>();
    }

    [Fact]
    public void Resolve_HandoffWithInvalidTargetGuid_SkipsInvalidTarget()
    {
        var validId = Guid.NewGuid();
        var json = $$"""
        {
            "handoff": {
                "targets": {
                    "billing": "{{validId}}",
                    "invalid": "not-a-guid"
                }
            }
        }
        """;

        var strategy = ExecutionStrategyResolver.Resolve(AgentExecutionMode.Handoff, json);
        strategy.ShouldBeOfType<HandoffExecutionStrategy>();
    }

    [Fact]
    public void Resolve_RouterMode_ReturnsRouterExecutionStrategy()
    {
        var targetId = Guid.NewGuid();
        var json = $$"""
        {
            "router": {
                "targets": {
                    "specialist": "{{targetId}}"
                },
                "allowDirectResponse": false
            }
        }
        """;

        var strategy = ExecutionStrategyResolver.Resolve(AgentExecutionMode.Router, json);
        strategy.ShouldBeOfType<RouterExecutionStrategy>();
    }

    [Fact]
    public void Resolve_RouterModeWithoutConfig_ReturnsRouterWithDefaults()
    {
        var strategy = ExecutionStrategyResolver.Resolve(AgentExecutionMode.Router, null);
        strategy.ShouldBeOfType<RouterExecutionStrategy>();
    }

    [Fact]
    public void Resolve_InvalidJsonConfig_ReturnsSingleAgentStrategy()
    {
        // Invalid JSON should be handled gracefully by AgentExecutionConfigDto.Deserialize
        var strategy = ExecutionStrategyResolver.Resolve(AgentExecutionMode.Single, "{invalid json}");
        strategy.ShouldBeOfType<SingleAgentStrategy>();
    }

    [Fact]
    public void Resolve_AgentAsToolsMode_ReturnsAgentAsToolsStrategy()
    {
        var childId = Guid.NewGuid();
        var json = $$"""
        {
            "agentAsTools": {
                "agents": {
                    "billing": "{{childId}}"
                }
            }
        }
        """;

        var strategy = ExecutionStrategyResolver.Resolve(AgentExecutionMode.AgentAsTools, json);
        strategy.ShouldBeOfType<AgentAsToolsExecutionStrategy>();
    }

    [Fact]
    public void Resolve_AgentAsToolsModeWithoutConfig_ReturnsAgentAsToolsWithDefaults()
    {
        var strategy = ExecutionStrategyResolver.Resolve(AgentExecutionMode.AgentAsTools, null);
        strategy.ShouldBeOfType<AgentAsToolsExecutionStrategy>();
    }

    [Fact]
    public void Resolve_HandoffWithAllowReturnToSource_ParsesCorrectly()
    {
        var agentId = Guid.NewGuid();
        var json = $$"""
        {
            "handoff": {
                "targets": {
                    "billing": "{{agentId}}"
                },
                "maxHandoffs": 5,
                "allowReturnToSource": false
            }
        }
        """;

        var strategy = ExecutionStrategyResolver.Resolve(AgentExecutionMode.Handoff, json);
        strategy.ShouldBeOfType<HandoffExecutionStrategy>();
    }
}
