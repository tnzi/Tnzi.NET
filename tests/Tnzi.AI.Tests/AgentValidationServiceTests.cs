using Microsoft.Extensions.DependencyInjection;

namespace Tnzi.AI.Tests;

/// <summary>
/// Agent 验证服务测试
/// </summary>
public class AgentValidationServiceTests
{
    private AgentValidationService CreateService(
        Mock<IRepository<Agent, Guid>>? agentRepo = null,
        Mock<IChatClientFactory>? chatClientFactory = null,
        Mock<IToolRegistry>? toolRegistry = null,
        ISkillRegistry? skillRegistry = null)
    {
        agentRepo ??= new Mock<IRepository<Agent, Guid>>();
        chatClientFactory ??= new Mock<IChatClientFactory>();
        toolRegistry ??= new Mock<IToolRegistry>();

        chatClientFactory.Setup(x => x.GetAvailableProviders()).Returns(["OpenAI", "Anthropic"]);
        chatClientFactory.Setup(x => x.GetDefaultModel(It.IsAny<string?>())).Returns("gpt-4");
        toolRegistry.Setup(x => x.GetAllGroupNames()).Returns(["web_search", "code_tools", "file_tools"]);

        var services = new ServiceCollection();
        services.AddLogging();

        return new AgentValidationService(
            agentRepo.Object,
            chatClientFactory.Object,
            toolRegistry.Object,
            services.BuildServiceProvider(),
            skillRegistry);
    }

    [Fact]
    public async Task ValidateAsync_ValidAgent_AllChecksPassed()
    {
        var agentId = Guid.NewGuid();
        var agent = new Agent
        {
            Id = agentId,
            Name = "Test Agent",
            Provider = "OpenAI",
            Model = "gpt-4",
            ToolGroups = new List<string> { "web_search", "code_tools" },
            ExecutionMode = AgentExecutionMode.Single
        };

        var agentRepo = new Mock<IRepository<Agent, Guid>>();
        agentRepo.Setup(x => x.GetAsync(agentId, It.IsAny<CancellationToken>())).ReturnsAsync(agent);

        var service = CreateService(agentRepo, skillRegistry: Mock.Of<ISkillRegistry>());
        var result = await service.ValidateAsync(agentId);

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data!.IsValid.ShouldBeTrue();
        result.Data.AgentName.ShouldBe("Test Agent");
        result.Data.Checks.ShouldAllBe(c => c.Passed);
    }

    [Fact]
    public async Task ValidateAsync_InvalidProvider_FailsProviderCheck()
    {
        var agentId = Guid.NewGuid();
        var agent = new Agent
        {
            Id = agentId,
            Name = "Test Agent",
            Provider = "NonExistent",
            ExecutionMode = AgentExecutionMode.Single
        };

        var agentRepo = new Mock<IRepository<Agent, Guid>>();
        agentRepo.Setup(x => x.GetAsync(agentId, It.IsAny<CancellationToken>())).ReturnsAsync(agent);

        var service = CreateService(agentRepo, skillRegistry: Mock.Of<ISkillRegistry>());
        var result = await service.ValidateAsync(agentId);

        result.Succeeded.ShouldBeTrue();
        result.Data!.IsValid.ShouldBeFalse();
        var providerCheck = result.Data.Checks.First(c => c.Name == "provider");
        providerCheck.Passed.ShouldBeFalse();
        providerCheck.Details.ShouldContain("NonExistent");
    }

    [Fact]
    public async Task ValidateAsync_MissingToolGroup_FailsToolGroupCheck()
    {
        var agentId = Guid.NewGuid();
        var agent = new Agent
        {
            Id = agentId,
            Name = "Test Agent",
            Provider = "OpenAI",
            Model = "gpt-4",
            ToolGroups = new List<string> { "web_search", "nonexistent_tools" },
            ExecutionMode = AgentExecutionMode.Single
        };

        var agentRepo = new Mock<IRepository<Agent, Guid>>();
        agentRepo.Setup(x => x.GetAsync(agentId, It.IsAny<CancellationToken>())).ReturnsAsync(agent);

        var service = CreateService(agentRepo, skillRegistry: Mock.Of<ISkillRegistry>());
        var result = await service.ValidateAsync(agentId);

        result.Succeeded.ShouldBeTrue();
        result.Data!.IsValid.ShouldBeFalse();
        var toolCheck = result.Data.Checks.First(c => c.Name == "tool_groups");
        toolCheck.Passed.ShouldBeFalse();
        toolCheck.Details.ShouldContain("nonexistent_tools");
    }

    [Fact]
    public async Task ValidateAsync_AgentNotFound_Returns404()
    {
        var agentRepo = new Mock<IRepository<Agent, Guid>>();
        agentRepo.Setup(x => x.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Agent?)null);

        var service = CreateService(agentRepo);
        var result = await service.ValidateAsync(Guid.NewGuid());

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
    }

    [Fact]
    public async Task ValidateAsync_NoModel_UsesDefault()
    {
        var agentId = Guid.NewGuid();
        var agent = new Agent
        {
            Id = agentId,
            Name = "Test Agent",
            Provider = "OpenAI",
            Model = null, // No model specified
            ExecutionMode = AgentExecutionMode.Single
        };

        var agentRepo = new Mock<IRepository<Agent, Guid>>();
        agentRepo.Setup(x => x.GetAsync(agentId, It.IsAny<CancellationToken>())).ReturnsAsync(agent);

        var service = CreateService(agentRepo, skillRegistry: Mock.Of<ISkillRegistry>());
        var result = await service.ValidateAsync(agentId);

        result.Succeeded.ShouldBeTrue();
        var modelCheck = result.Data!.Checks.First(c => c.Name == "model");
        modelCheck.Passed.ShouldBeTrue();
        modelCheck.Details.ShouldContain("default");
    }

    [Fact]
    public async Task ValidateAsync_NoToolGroups_PassesCheck()
    {
        var agentId = Guid.NewGuid();
        var agent = new Agent
        {
            Id = agentId,
            Name = "Test Agent",
            Provider = "OpenAI",
            ToolGroups = null,
            ExecutionMode = AgentExecutionMode.Single
        };

        var agentRepo = new Mock<IRepository<Agent, Guid>>();
        agentRepo.Setup(x => x.GetAsync(agentId, It.IsAny<CancellationToken>())).ReturnsAsync(agent);

        var service = CreateService(agentRepo, skillRegistry: Mock.Of<ISkillRegistry>());
        var result = await service.ValidateAsync(agentId);

        result.Succeeded.ShouldBeTrue();
        var toolCheck = result.Data!.Checks.First(c => c.Name == "tool_groups");
        toolCheck.Passed.ShouldBeTrue();
    }

    [Fact]
    public async Task ValidateAsync_NullToolGroups_PassesCheck()
    {
        var agentId = Guid.NewGuid();
        var agent = new Agent
        {
            Id = agentId,
            Name = "Test Agent",
            Provider = "OpenAI",
            ToolGroups = null,
            ExecutionMode = AgentExecutionMode.Single
        };

        var agentRepo = new Mock<IRepository<Agent, Guid>>();
        agentRepo.Setup(x => x.GetAsync(agentId, It.IsAny<CancellationToken>())).ReturnsAsync(agent);

        var service = CreateService(agentRepo, skillRegistry: Mock.Of<ISkillRegistry>());
        var result = await service.ValidateAsync(agentId);

        result.Succeeded.ShouldBeTrue();
        var toolCheck = result.Data!.Checks.First(c => c.Name == "tool_groups");
        toolCheck.Passed.ShouldBeTrue();
    }

    [Fact]
    public void ValidationCheckDto_Properties()
    {
        var check = new ValidationCheckDto
        {
            Name = "test",
            Passed = true,
            Details = "OK"
        };

        check.Name.ShouldBe("test");
        check.Passed.ShouldBeTrue();
        check.Details.ShouldBe("OK");
    }

    [Fact]
    public void AgentHealthSummaryDto_DefaultValues()
    {
        var summary = new AgentHealthSummaryDto();
        summary.TotalAgents.ShouldBe(0);
        summary.HealthyAgents.ShouldBe(0);
        summary.UnhealthyAgents.ShouldBe(0);
        summary.DisabledAgents.ShouldBe(0);
        summary.UnhealthyDetails.ShouldBeEmpty();
    }

    [Fact]
    public void WorkflowDefinitionQueryDto_Properties()
    {
        var query = new WorkflowDefinitionQueryDto
        {
            Keyword = "test",
            IsEnabled = true,
            ExecutionMode = WorkflowExecutionMode.Sequential
        };

        query.Keyword.ShouldBe("test");
        query.IsEnabled.ShouldBe(true);
        query.ExecutionMode.ShouldBe(WorkflowExecutionMode.Sequential);
    }
}
