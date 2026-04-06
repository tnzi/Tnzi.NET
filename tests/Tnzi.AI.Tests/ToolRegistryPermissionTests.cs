using Tnzi.Security.Authorization;
using Tnzi.AI.Tools.Models;

namespace Tnzi.AI.Tests;

/// <summary>
/// ToolRegistry 与权限过滤链路回归测试
/// </summary>
public class ToolRegistryPermissionTests
{
    private sealed class BuiltInTextTools : IAIToolProvider;
    private sealed class UserTextTools : IAIToolProvider;

    [Fact]
    public void UnregisterByProviderType_RemovesOnlyMatchingProviderTools()
    {
        var registry = new ToolRegistry(Mock.Of<ILogger<ToolRegistry>>());

        registry.Register(new ToolDefinition
        {
            Name = "builtin_text_tool",
            GroupName = "text",
            ProviderType = typeof(BuiltInTextTools),
            RequiredPermissions = []
        });

        registry.Register(new ToolDefinition
        {
            Name = "user_text_tool",
            GroupName = "text",
            ProviderType = typeof(UserTextTools),
            RequiredPermissions = []
        });

        registry.UnregisterByProviderType(typeof(BuiltInTextTools));

        var allTools = registry.GetAllTools();
        allTools.Count.ShouldBe(1);
        allTools[0].Name.ShouldBe("user_text_tool");
        allTools[0].ProviderType.ShouldBe(typeof(UserTextTools));
    }

    [Fact]
    public void GetToolsByGroupsWithPermissions_FiltersByRequiredPermissions()
    {
        var registry = new ToolRegistry(Mock.Of<ILogger<ToolRegistry>>());

        registry.Register(new ToolDefinition
        {
            Name = "no_perm_tool",
            GroupName = "group-a",
            ProviderType = typeof(BuiltInTextTools),
            RequiredPermissions = []
        });

        registry.Register(new ToolDefinition
        {
            Name = "need_read",
            GroupName = "group-a",
            ProviderType = typeof(BuiltInTextTools),
            RequiredPermissions = ["perm.read"]
        });

        registry.Register(new ToolDefinition
        {
            Name = "need_read_write",
            GroupName = "group-a",
            ProviderType = typeof(BuiltInTextTools),
            RequiredPermissions = ["perm.read", "perm.write"]
        });

        var filtered = registry.GetToolsByGroupsWithPermissions(
            ["group-a"],
            userPermissions: ["perm.read"]);

        filtered.Select(t => t.Name).OrderBy(x => x).ToArray()
            .ShouldBe(["need_read", "no_perm_tool"], ignoreOrder: false);
    }

    [Fact]
    public async Task ResolveAgentAsync_PassesGrantedPermissionsToAgentFactory()
    {
        var agentId = Guid.NewGuid();
        var capturedPermissions = Array.Empty<string>();
        Guid? capturedAgentId = null;

        var agentFactory = new Mock<IAgentFactory>();
        agentFactory.Setup(f => f.CreateAgentAsync(
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<IEnumerable<string>?>(),
                It.IsAny<double?>(),
                It.IsAny<int?>(),
                It.IsAny<AgentExecutorOptions?>(),
                It.IsAny<IEnumerable<string>?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string?, string?, string?, string?, IEnumerable<string>?, double?, int?, AgentExecutorOptions?, IEnumerable<string>?, Guid?, CancellationToken>(
                (_, _, _, _, _, _, _, _, userPermissions, resolvedAgentId, _) =>
                {
                    capturedPermissions = userPermissions?.OrderBy(x => x).ToArray() ?? [];
                    capturedAgentId = resolvedAgentId;
                })
            .ReturnsAsync(new AgentExecutor(Mock.Of<IChatClient>(), new AgentExecutorOptions { Name = "test" }));

        var agentRepository = new Mock<IRepository<Agent, Guid>>();
        agentRepository.Setup(r => r.GetAsync(agentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Agent
            {
                Id = agentId,
                Name = "agent",
                Provider = "OpenAI",
                Model = "gpt-4o",
                ToolGroups = new List<string> { "group-a" },
                IsEnabled = true
            });

        var toolRegistry = new ToolRegistry(Mock.Of<ILogger<ToolRegistry>>());
        toolRegistry.Register(new ToolDefinition
        {
            Name = "tool_need_read",
            GroupName = "group-a",
            ProviderType = typeof(BuiltInTextTools),
            RequiredPermissions = ["perm.read"]
        });
        toolRegistry.Register(new ToolDefinition
        {
            Name = "tool_need_write",
            GroupName = "group-a",
            ProviderType = typeof(BuiltInTextTools),
            RequiredPermissions = ["perm.write"]
        });

        var permissionChecker = new Mock<IPermissionChecker>();
        permissionChecker.Setup(p => p.IsGrantedAsync("perm.read")).ReturnsAsync(true);
        permissionChecker.Setup(p => p.IsGrantedAsync("perm.write")).ReturnsAsync(false);

        var aiOptions = Microsoft.Extensions.Options.Options.Create(new AIOptions
        {
            DefaultProvider = "OpenAI",
            Providers = new Dictionary<string, ProviderOptions>
            {
                ["OpenAI"] = new() { Enabled = true, ApiKey = "sk-test-12345678901234567890", DefaultModel = "gpt-4o" }
            }
        });

        var versionRouter = new Mock<IAgentVersionRouter>();
        versionRouter.Setup(r => r.RouteAsync(It.IsAny<Agent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Agent a, CancellationToken _) => AgentVersionRouteResult.Passthrough(a));

        var resolver = new AgentResolver(
            agentFactory.Object,
            aiOptions,
            agentRepository.Object,
            toolRegistry,
            new SimplePromptTemplateEngine(),
            versionRouter.Object,
            Mock.Of<ILogger<AgentResolver>>(),
            permissionChecker: permissionChecker.Object);

        var resolution = await resolver.ResolveAgentAsync(agentId, null, null, null, CancellationToken.None);

        resolution.IsSuccess.ShouldBeTrue();
        capturedPermissions.ShouldBe(["perm.read"]);
        capturedAgentId.ShouldBe(agentId);
    }
}
