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
                It.IsAny<CancellationToken>()))
            .Callback<string?, string?, string?, string?, IEnumerable<string>?, double?, int?, AgentExecutorOptions?, IEnumerable<string>?, CancellationToken>(
                (_, _, _, _, _, _, _, _, userPermissions, _) =>
                {
                    capturedPermissions = userPermissions?.OrderBy(x => x).ToArray() ?? [];
                })
            .ReturnsAsync(new AgentExecutor(Mock.Of<IChatClient>(), new AgentExecutorOptions { Name = "test" }));

        var threadService = new Mock<IAgentThreadInternalService>();
        var usageLogService = new Mock<IUsageLogService>();
        var quotaService = new Mock<IQuotaService>();
        var tokenEstimator = new Mock<ITokenEstimator>();

        var agentRepository = new Mock<IRepository<Agent, Guid>>();
        agentRepository.Setup(r => r.GetAsync(agentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Agent
            {
                Id = agentId,
                Name = "agent",
                Provider = "OpenAI",
                Model = "gpt-4o",
                ToolGroups = JsonSerializer.Serialize(new List<string> { "group-a" }),
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

        var services = new ServiceCollection();
        services.AddSingleton<IToolRegistry>(toolRegistry);
        services.AddSingleton(permissionChecker.Object);
        var serviceProvider = services.BuildServiceProvider();

        var aiOptions = Microsoft.Extensions.Options.Options.Create(new AIOptions
        {
            DefaultProvider = "OpenAI",
            Providers = new Dictionary<string, ProviderOptions>
            {
                ["OpenAI"] = new() { Enabled = true, ApiKey = "sk-test-12345678901234567890", DefaultModel = "gpt-4o" }
            }
        });

        var guardrailRunner = new GuardrailRunner(
            [],
            [],
            aiOptions,
            Mock.Of<ILogger<GuardrailRunner>>());

        var pipeline = new ChatExecutionPipeline(
            agentFactory.Object,
            threadService.Object,
            usageLogService.Object,
            quotaService.Object,
            tokenEstimator.Object,
            guardrailRunner,
            aiOptions,
            agentRepository.Object,
            serviceProvider,
            Mock.Of<ILogger<ChatExecutionPipeline>>());

        var resolution = await pipeline.ResolveAgentAsync(agentId, null, null, null, CancellationToken.None);

        resolution.IsSuccess.ShouldBeTrue();
        capturedPermissions.ShouldBe(["perm.read"]);
    }
}
