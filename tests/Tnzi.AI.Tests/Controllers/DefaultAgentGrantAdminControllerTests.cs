using Tnzi.AI.Controllers.Admin;

namespace Tnzi.AI.Tests.Controllers;

/// <summary>
/// DefaultAgentGrantAdminController - additive governance surface over IAgentGrantService:
/// reverse query ("which agents use resource X") + per-grant enable/priority.
/// </summary>
public class DefaultAgentGrantAdminControllerTests
{
    private readonly Mock<IAgentGrantService> _grantService = new();
    private readonly DefaultAgentGrantAdminController _controller;

    public DefaultAgentGrantAdminControllerTests()
    {
        _controller = new DefaultAgentGrantAdminController(_grantService.Object);
    }

    private static AgentGrantUsageDto Usage(Guid id, string name = "Agent") => new() { AgentId = id, AgentName = name, AgentIsEnabled = true };

    [Fact]
    public async Task ReverseTool_ReturnsAgents()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        _grantService.Setup(s => s.GetAgentsUsingToolAsync("fs", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Usage(a, "A"), Usage(b, "B") });

        var result = await _controller.ReverseByTool("fs");

        result.Succeeded.ShouldBeTrue();
        result.Data!.Select(x => x.AgentId).ShouldBe(new[] { a, b });
    }

    [Fact]
    public async Task ReverseTool_BlankKey_Returns400()
    {
        var result = await _controller.ReverseByTool(" ");

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    [Fact]
    public async Task ReverseSkill_ReturnsAgentIds()
    {
        var a = Guid.NewGuid();
        _grantService.Setup(s => s.GetAgentsUsingSkillAsync("writing", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Usage(a) });

        var result = await _controller.ReverseBySkill("writing");

        result.Succeeded.ShouldBeTrue();
        result.Data!.Select(x => x.AgentId).ShouldBe(new[] { a });
    }

    [Fact]
    public async Task ReverseKnowledge_ReturnsAgentIds()
    {
        var kbId = Guid.NewGuid();
        var a = Guid.NewGuid();
        _grantService.Setup(s => s.GetAgentsUsingKnowledgeAsync(kbId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Usage(a) });

        var result = await _controller.ReverseByKnowledge(kbId);

        result.Succeeded.ShouldBeTrue();
        result.Data!.Select(x => x.AgentId).ShouldBe(new[] { a });
    }

    [Fact]
    public async Task ListForAgent_ReturnsServiceList()
    {
        var agentId = Guid.NewGuid();
        var list = new AgentGrantListDto { Skills = [new AgentGrantDto { Id = Guid.NewGuid(), Key = "writing", IsEnabled = false }] };
        _grantService.Setup(s => s.ListGrantsAsync(agentId, It.IsAny<CancellationToken>())).ReturnsAsync(list);

        var result = await _controller.ListForAgent(agentId);

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldBeSameAs(list);
    }

    [Fact]
    public async Task Delete_Found_ReturnsOk_Missing_Returns404()
    {
        var grantId = Guid.NewGuid();
        _grantService.Setup(s => s.DeleteGrantAsync(GrantResourceType.Skill, grantId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        (await _controller.Delete(GrantResourceType.Skill, grantId)).Succeeded.ShouldBeTrue();

        var missing = await _controller.Delete(GrantResourceType.Tool, Guid.NewGuid());
        missing.Succeeded.ShouldBeFalse();
        missing.Code.ShouldBe(404);
    }

    [Fact]
    public async Task SetEnabled_Found_ReturnsOk()
    {
        var grantId = Guid.NewGuid();
        _grantService.Setup(s => s.SetGrantEnabledAsync(GrantResourceType.Tool, grantId, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _controller.SetEnabled(GrantResourceType.Tool, grantId, new SetGrantEnabledDto { Enabled = false });

        result.Succeeded.ShouldBeTrue();
        _grantService.Verify(s => s.SetGrantEnabledAsync(GrantResourceType.Tool, grantId, false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetEnabled_NotFound_Returns404()
    {
        var grantId = Guid.NewGuid();
        _grantService.Setup(s => s.SetGrantEnabledAsync(It.IsAny<GrantResourceType>(), It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _controller.SetEnabled(GrantResourceType.Skill, grantId, new SetGrantEnabledDto { Enabled = true });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
    }

    [Fact]
    public async Task SetPriority_Found_ReturnsOk()
    {
        var grantId = Guid.NewGuid();
        _grantService.Setup(s => s.SetGrantPriorityAsync(GrantResourceType.Knowledge, grantId, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _controller.SetPriority(GrantResourceType.Knowledge, grantId, new SetGrantPriorityDto { Priority = 100 });

        result.Succeeded.ShouldBeTrue();
        _grantService.Verify(s => s.SetGrantPriorityAsync(GrantResourceType.Knowledge, grantId, 100, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetPriority_NotFound_Returns404()
    {
        _grantService.Setup(s => s.SetGrantPriorityAsync(It.IsAny<GrantResourceType>(), It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _controller.SetPriority(GrantResourceType.Tool, Guid.NewGuid(), new SetGrantPriorityDto { Priority = 5 });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
    }
}
