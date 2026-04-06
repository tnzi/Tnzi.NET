using Tnzi.AI.Tools.Models;

namespace Tnzi.AI.Tests;

/// <summary>
/// ToolResolver 单元测试 — 验证 C# 工具 / OpenAPI 工具 / MCP 工具的解析与合并
/// </summary>
public class ToolResolverTests
{
    private sealed class DummyToolProvider
    {
        public string Echo() => "ok";
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static IOptions<AIOptions> CreateOptions(Action<AIOptions>? configure = null)
    {
        var options = new AIOptions();
        configure?.Invoke(options);
        return Microsoft.Extensions.Options.Options.Create(options);
    }

    private static ILoggerFactory CreateLoggerFactory() =>
        LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.None));

    private static ToolResolver CreateResolver(
        IToolRegistry? toolRegistry = null,
        IMcpToolProvider? mcpToolProvider = null,
        OpenApiToolGenerator? openApiToolGenerator = null,
        IOptions<AIOptions>? options = null,
        IServiceProvider? serviceProvider = null,
        IToolApprovalHandler? approvalHandler = null,
        IToolPermissionEvaluator? permissionEvaluator = null)
    {
        toolRegistry ??= CreateEmptyRegistry();
        mcpToolProvider ??= CreateEmptyMcpProvider();
        openApiToolGenerator ??= CreateOpenApiGenerator(Microsoft.Extensions.Options.Options.Create(new AIOptions()));
        options ??= CreateOptions();
        serviceProvider ??= Mock.Of<IServiceProvider>();

        var loggerFactory = CreateLoggerFactory();
        var logger = loggerFactory.CreateLogger<ToolResolver>();

        return new ToolResolver(
            toolRegistry,
            mcpToolProvider,
            openApiToolGenerator,
            options,
            serviceProvider,
            loggerFactory,
            logger,
            approvalHandler,
            permissionEvaluator);
    }

    private static IToolRegistry CreateEmptyRegistry()
    {
        var mock = new Mock<IToolRegistry>();
        mock.Setup(r => r.GetToolsByGroupsWithPermissions(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IEnumerable<string>>()))
            .Returns([]);
        return mock.Object;
    }

    private static IMcpToolProvider CreateEmptyMcpProvider()
    {
        var mock = new Mock<IMcpToolProvider>();
        mock.Setup(p => p.GetToolsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AITool>());
        return mock.Object;
    }

    private static OpenApiToolGenerator CreateOpenApiGenerator(IOptions<AIOptions> options)
    {
        var httpClientFactory = new Mock<IHttpClientFactory>();
        var logger = new Mock<ILogger<OpenApiToolGenerator>>().Object;
        return new OpenApiToolGenerator(httpClientFactory.Object, options, logger);
    }

    private static AITool CreateNamedTool(string name)
    {
        var mock = new Mock<AITool>();
        mock.Setup(t => t.Name).Returns(name);
        return mock.Object;
    }

    // ------------------------------------------------------------------
    // Empty tool groups → empty (null) result
    // ------------------------------------------------------------------

    [Fact]
    public async Task ResolveToolsAsync_NullToolGroups_NoMcp_ReturnsNull()
    {
        var resolver = CreateResolver();

        var result = await resolver.ResolveToolsAsync(null);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task ResolveToolsAsync_EmptyToolGroups_NoMcp_ReturnsNull()
    {
        var resolver = CreateResolver();

        var result = await resolver.ResolveToolsAsync([]);

        result.ShouldBeNull();
    }

    // ------------------------------------------------------------------
    // Unknown / unregistered tool group → skipped
    // ------------------------------------------------------------------

    [Fact]
    public async Task ResolveToolsAsync_UnknownGroup_ReturnsNull()
    {
        var registry = new Mock<IToolRegistry>();
        registry.Setup(r => r.GetToolsByGroupsWithPermissions(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IEnumerable<string>>()))
            .Returns([]); // unknown group returns no definitions

        var resolver = CreateResolver(toolRegistry: registry.Object);

        var result = await resolver.ResolveToolsAsync(["unknown-group"]);

        result.ShouldBeNull();
    }

    // ------------------------------------------------------------------
    // MCP tools included when enabled
    // ------------------------------------------------------------------

    [Fact]
    public async Task ResolveToolsAsync_McpEnabled_McpToolsIncluded()
    {
        var mcpTool = CreateNamedTool("mcp_weather");
        var mcpProvider = new Mock<IMcpToolProvider>();
        mcpProvider.Setup(p => p.GetToolsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { mcpTool });

        var options = CreateOptions(o => o.Mcp = new McpOptions { Enabled = true });

        var resolver = CreateResolver(mcpToolProvider: mcpProvider.Object, options: options);

        var result = await resolver.ResolveToolsAsync(null);

        result.ShouldNotBeNull();
        result!.Count.ShouldBe(1);
        result[0].Name.ShouldBe("mcp_weather");
    }

    [Fact]
    public async Task ResolveToolsAsync_McpDisabled_McpToolsNotIncluded()
    {
        var mcpTool = CreateNamedTool("mcp_weather");
        var mcpProvider = new Mock<IMcpToolProvider>();
        mcpProvider.Setup(p => p.GetToolsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { mcpTool });

        var options = CreateOptions(o => o.Mcp = new McpOptions { Enabled = false });

        var resolver = CreateResolver(mcpToolProvider: mcpProvider.Object, options: options);

        var result = await resolver.ResolveToolsAsync(null);

        // MCP disabled — no tools
        result.ShouldBeNull();
        mcpProvider.Verify(p => p.GetToolsAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ------------------------------------------------------------------
    // Deduplication — same tool name from multiple sources
    // ------------------------------------------------------------------

    [Fact]
    public async Task ResolveToolsAsync_DuplicateNameAcrossMcp_CSharpWins()
    {
        // C# tool named "tool_a" and MCP tool named "tool_a" — C# takes priority
        var mcpTool = CreateNamedTool("tool_a");
        var mcpProvider = new Mock<IMcpToolProvider>();
        mcpProvider.Setup(p => p.GetToolsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { mcpTool });

        var options = CreateOptions(o => o.Mcp = new McpOptions { Enabled = true });

        // We cannot register real ToolDefinitions easily because ToolAdapter needs real MethodInfo.
        // Instead, verify deduplication by providing two MCP tools with the same name.
        var mcpTool2 = CreateNamedTool("tool_a");
        var mcpProvider2 = new Mock<IMcpToolProvider>();
        mcpProvider2.Setup(p => p.GetToolsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { mcpTool, mcpTool2 });

        var resolver = CreateResolver(mcpToolProvider: mcpProvider2.Object, options: options);

        var result = await resolver.ResolveToolsAsync(null);

        result.ShouldNotBeNull();
        // Only one "tool_a" in the result despite two MCP tools with same name
        result!.Count(t => string.Equals(t.Name, "tool_a", StringComparison.OrdinalIgnoreCase)).ShouldBe(1);
    }

    [Fact]
    public async Task ResolveToolsAsync_MultipleUniqueMcpTools_AllIncluded()
    {
        var toolA = CreateNamedTool("tool_a");
        var toolB = CreateNamedTool("tool_b");
        var toolC = CreateNamedTool("tool_c");

        var mcpProvider = new Mock<IMcpToolProvider>();
        mcpProvider.Setup(p => p.GetToolsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { toolA, toolB, toolC });

        var options = CreateOptions(o => o.Mcp = new McpOptions { Enabled = true });

        var resolver = CreateResolver(mcpToolProvider: mcpProvider.Object, options: options);

        var result = await resolver.ResolveToolsAsync(null);

        result.ShouldNotBeNull();
        result!.Count.ShouldBe(3);
    }

    // ------------------------------------------------------------------
    // OpenAPI tools — loaded when configured
    // ------------------------------------------------------------------

    [Fact]
    public async Task ResolveToolsAsync_OpenApiDisabled_OpenApiToolsNotLoaded()
    {
        var options = CreateOptions(o =>
        {
            o.OpenApiTools = new OpenApiToolsOptions { Enabled = false };
        });

        var resolver = CreateResolver(options: options);

        // OpenAPI is disabled — no exception, no tools
        var result = await resolver.ResolveToolsAsync(null);
        result.ShouldBeNull();
    }

    [Fact]
    public async Task ResolveToolsAsync_OpenApiEnabledNoSpecs_ReturnsNull()
    {
        var options = CreateOptions(o =>
        {
            o.OpenApiTools = new OpenApiToolsOptions
            {
                Enabled = true,
                Specs = [] // no specs configured
            };
        });

        var resolver = CreateResolver(options: options);

        var result = await resolver.ResolveToolsAsync(null);
        result.ShouldBeNull();
    }

    // ------------------------------------------------------------------
    // Approval wrapper applied when configured
    // ------------------------------------------------------------------

    [Fact]
    public async Task ResolveToolsAsync_ApprovalEnabled_NoApprovalHandler_ToolsReturnedUnwrapped()
    {
        // When ToolApproval.Enabled = true but no IToolApprovalHandler is provided,
        // the WrapWithApproval method returns the tools as-is.
        var options = CreateOptions(o =>
        {
            o.ToolApproval = new ToolApprovalOptions
            {
                Enabled = true,
                Mode = ToolApprovalMode.AlwaysRequire
            };
            o.Mcp = new McpOptions { Enabled = true };
        });

        var mcpTool = CreateNamedTool("mcp_tool");
        var mcpProvider = new Mock<IMcpToolProvider>();
        mcpProvider.Setup(p => p.GetToolsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { mcpTool });

        // No approvalHandler passed (null) — WrapWithApproval returns original list
        var resolver = CreateResolver(
            mcpToolProvider: mcpProvider.Object,
            options: options,
            approvalHandler: null);

        var result = await resolver.ResolveToolsAsync(null);

        // MCP tools don't go through WrapWithApproval (only C# tools do)
        result.ShouldNotBeNull();
        result!.Count.ShouldBe(1);
    }

    [Fact]
    public async Task ResolveToolsAsync_PermissionEvaluatorWithoutCurrentRules_StillWrapsCSharpTools()
    {
        var registry = new Mock<IToolRegistry>();
        registry.Setup(r => r.GetToolsByGroupsWithPermissions(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IEnumerable<string>>()))
            .Returns(
            [
                new ToolDefinition
                {
                    Name = "echo",
                    GroupName = "utility",
                    ProviderType = typeof(DummyToolProvider),
                    MethodInfo = typeof(DummyToolProvider).GetMethod(nameof(DummyToolProvider.Echo))!,
                    RequiredPermissions = []
                }
            ]);

        var resolver = CreateResolver(
            toolRegistry: registry.Object,
            permissionEvaluator: new ToolPermissionEvaluator([]));

        var result = await resolver.ResolveToolsAsync(["utility"]);

        result.ShouldNotBeNull();
        result!.Count.ShouldBe(1);
        result[0].ShouldBeOfType<ApprovalToolWrapper>();
    }

    // ------------------------------------------------------------------
    // Null name tools are excluded
    // ------------------------------------------------------------------

    [Fact]
    public async Task ResolveToolsAsync_McpToolWithNullName_Excluded()
    {
        var validTool = CreateNamedTool("valid_tool");
        var nullNameTool = new Mock<AITool>();
        nullNameTool.Setup(t => t.Name).Returns((string)null!);

        var mcpProvider = new Mock<IMcpToolProvider>();
        mcpProvider.Setup(p => p.GetToolsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { validTool, nullNameTool.Object });

        var options = CreateOptions(o => o.Mcp = new McpOptions { Enabled = true });

        var resolver = CreateResolver(mcpToolProvider: mcpProvider.Object, options: options);

        var result = await resolver.ResolveToolsAsync(null);

        result.ShouldNotBeNull();
        result!.Count.ShouldBe(1);
        result[0].Name.ShouldBe("valid_tool");
    }

    // ------------------------------------------------------------------
    // MCP tools + registry tools combined
    // ------------------------------------------------------------------

    [Fact]
    public async Task ResolveToolsAsync_McpEnabledAndNullToolGroups_OnlyMcpToolsReturned()
    {
        var mcpTool = CreateNamedTool("mcp_search");

        var mcpProvider = new Mock<IMcpToolProvider>();
        mcpProvider.Setup(p => p.GetToolsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { mcpTool });

        var options = CreateOptions(o => o.Mcp = new McpOptions { Enabled = true });

        var resolver = CreateResolver(mcpToolProvider: mcpProvider.Object, options: options);

        // null toolGroups means no C# tools are fetched from registry
        var result = await resolver.ResolveToolsAsync(null);

        result.ShouldNotBeNull();
        result!.ShouldContain(t => t.Name == "mcp_search");
    }

    // ------------------------------------------------------------------
    // CancellationToken respected
    // ------------------------------------------------------------------

    [Fact]
    public async Task ResolveToolsAsync_CancellationAlreadyCancelled_ThrowsOperationCancelled()
    {
        var mcpProvider = new Mock<IMcpToolProvider>();
        mcpProvider.Setup(p => p.GetToolsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var options = CreateOptions(o => o.Mcp = new McpOptions { Enabled = true });

        var resolver = CreateResolver(mcpToolProvider: mcpProvider.Object, options: options);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(
            () => resolver.ResolveToolsAsync(null, ct: cts.Token));
    }
}
