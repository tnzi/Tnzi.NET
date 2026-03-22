namespace Tnzi.AI.Cli.Tests;

public class AgentResolutionTests
{
    [Fact]
    public void SuccessWithoutExecutor_ShouldSetIsSuccessTrue()
    {
        // Arrange & Act
        var resolution = AgentResolution.SuccessWithoutExecutor(
            "claude-code", "claude-sonnet-4-20250514", null, null, AgentExecutionMode.ExternalCli);

        // Assert
        resolution.IsSuccess.ShouldBeTrue();
        resolution.Agent.ShouldBeNull();
    }

    [Fact]
    public void SuccessWithoutExecutor_ShouldPreserveAllProperties()
    {
        // Arrange
        var agentId = Guid.NewGuid();
        var config = """{"externalCli":{"provider":"claude-code"}}""";

        // Act
        var resolution = AgentResolution.SuccessWithoutExecutor(
            "claude-code", "claude-sonnet-4-20250514", agentId, config, AgentExecutionMode.ExternalCli);

        // Assert
        resolution.Provider.ShouldBe("claude-code");
        resolution.Model.ShouldBe("claude-sonnet-4-20250514");
        resolution.AgentId.ShouldBe(agentId);
        resolution.AgentConfiguration.ShouldBe(config);
        resolution.ExecutionMode.ShouldBe(AgentExecutionMode.ExternalCli);
        resolution.Agent.ShouldBeNull();
        resolution.CreationParameters.ShouldBeNull();
    }

    [Fact]
    public void IsSuccess_ShouldBeFalseForFailure()
    {
        // Arrange & Act
        var resolution = AgentResolution.Failure("openai", "gpt-4o", null, "agent_not_found");

        // Assert
        resolution.IsSuccess.ShouldBeFalse();
        resolution.Agent.ShouldBeNull();
        resolution.ErrorCode.ShouldBe("agent_not_found");
    }
}
