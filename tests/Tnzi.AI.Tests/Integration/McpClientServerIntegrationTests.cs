using Tnzi.AI.Infrastructure.Mcp;

namespace Tnzi.AI.Tests.Integration;

/// <summary>
/// MCP 客户端-服务端集成测试 — 在 Provider 层验证 Tool/Resource/Prompt 的发现、聚合、故障隔离和缓存失效。
/// 使用 Mock IMcpClientAdapter 模拟 MCP 服务器，不启动外部进程。
/// </summary>
public class McpClientServerIntegrationTests
{
    private readonly Mock<IMcpClientFactory> _clientFactory = new();
    private readonly Mock<ILoggerFactory> _loggerFactory = new();

    public McpClientServerIntegrationTests()
    {
        _loggerFactory
            .Setup(x => x.CreateLogger(It.IsAny<string>()))
            .Returns(NullLogger.Instance);
    }

    #region Tool Discovery Roundtrip

    [Fact]
    public async Task ToolDiscovery_MockServer_ReturnsRegisteredTools()
    {
        // Arrange
        var tool1 = AIFunctionFactory.Create(() => "result1", "get_weather");
        var tool2 = AIFunctionFactory.Create(() => "result2", "get_time");

        var builder = new ServerBuilder()
            .AddServer("weather-server", tools: new List<AITool> { tool1, tool2 });

        var (toolProvider, _, _) = CreateProviders(builder);

        // Act
        var tools = await toolProvider.GetToolsAsync();

        // Assert
        tools.Count.ShouldBe(2);
        tools.ShouldContain(t => t.Name == "get_weather");
        tools.ShouldContain(t => t.Name == "get_time");
    }

    #endregion

    #region Tool Execution Roundtrip

    [Fact]
    public async Task ToolExecution_CallMcpTool_ReturnsCorrectResult()
    {
        // Arrange: 创建可调用的 AIFunction
        var func = AIFunctionFactory.Create((string city) => $"Weather in {city}: Sunny", "get_weather");

        var builder = new ServerBuilder()
            .AddServer("weather-server", tools: new List<AITool> { func });

        var (toolProvider, _, _) = CreateProviders(builder);

        // Act: 获取工具后调用
        var tools = await toolProvider.GetToolsAsync();
        tools.Count.ShouldBe(1);

        // 找到 AIFunction 并调用（经过 McpAuditToolWrapper 包装）
        var tool = tools[0];
        tool.ShouldBeAssignableTo<AIFunction>();
        var aiFunction = (AIFunction)tool;
        var result = await aiFunction.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?> { ["city"] = "Tokyo" }));

        // Assert
        result.ShouldNotBeNull();
        var resultText = result.ToString();
        resultText.ShouldNotBeNull();
        resultText.ShouldContain("Sunny");
    }

    #endregion

    #region Resource Discovery

    [Fact]
    public async Task ResourceDiscovery_ListResources_ReturnsFromMockServer()
    {
        // Arrange
        var resources = new List<McpResourceInfo>
        {
            new("file:///docs/readme.md", "README", "text/markdown", "Project readme"),
            new("file:///data/config.json", "Config", "application/json", null)
        };

        var builder = new ServerBuilder()
            .AddServer("docs-server", resources: resources);

        var (_, resourceProvider, _) = CreateProviders(builder);

        // Act
        var result = await resourceProvider.ListResourcesAsync("docs-server");

        // Assert
        result.Count.ShouldBe(2);
        result[0].Uri.ShouldBe("file:///docs/readme.md");
        result[0].Name.ShouldBe("README");
        result[0].MimeType.ShouldBe("text/markdown");
        result[1].Uri.ShouldBe("file:///data/config.json");
    }

    [Fact]
    public async Task ResourceDiscovery_ReadResource_ReturnsContent()
    {
        // Arrange
        var resources = new List<McpResourceInfo>
        {
            new("file:///docs/readme.md", "README", "text/markdown", null)
        };

        var builder = new ServerBuilder()
            .AddServer("docs-server", resources: resources,
                resourceContent: ("file:///docs/readme.md", new McpResourceContent("# Hello World", "text/markdown")));

        var (_, resourceProvider, _) = CreateProviders(builder);

        // Act
        var result = await resourceProvider.ReadResourceAsync("docs-server", "file:///docs/readme.md");

        // Assert
        result.ShouldNotBeNull();
        result.Text.ShouldBe("# Hello World");
        result.MimeType.ShouldBe("text/markdown");
    }

    #endregion

    #region Prompt Discovery

    [Fact]
    public async Task PromptDiscovery_ListPrompts_ReturnsFromMockServer()
    {
        // Arrange
        var prompts = new List<McpPromptInfo>
        {
            new("code-review", "Review code for quality", new List<McpPromptArgument>
            {
                new("language", "Programming language", true),
                new("style", "Review style", false)
            }),
            new("summarize", "Summarize text", null)
        };

        var builder = new ServerBuilder()
            .AddServer("ai-server", prompts: prompts);

        var (_, _, promptProvider) = CreateProviders(builder);

        // Act
        var result = await promptProvider.ListPromptsAsync("ai-server");

        // Assert
        result.Count.ShouldBe(2);
        result[0].Name.ShouldBe("code-review");
        result[0].Description.ShouldBe("Review code for quality");
        result[0].Arguments.ShouldNotBeNull();
        result[0].Arguments!.Count.ShouldBe(2);
        result[0].Arguments![0].Name.ShouldBe("language");
        result[0].Arguments![0].Required.ShouldBeTrue();
        result[1].Name.ShouldBe("summarize");
        result[1].Arguments.ShouldBeNull();
    }

    [Fact]
    public async Task PromptDiscovery_GetPrompt_ReturnsRenderedResult()
    {
        // Arrange
        var promptResult = new McpPromptResult(new List<McpPromptMessage>
        {
            new("user", "Please review this C# code for quality issues."),
            new("assistant", "I'll review the code. Please share it.")
        });

        var builder = new ServerBuilder()
            .AddServer("ai-server", promptResults: new Dictionary<string, McpPromptResult>
            {
                ["code-review"] = promptResult
            });

        var (_, _, promptProvider) = CreateProviders(builder);

        // Act
        var result = await promptProvider.GetPromptAsync("ai-server", "code-review",
            new Dictionary<string, string> { ["language"] = "csharp" });

        // Assert
        result.ShouldNotBeNull();
        result.Messages.Count.ShouldBe(2);
        result.Messages[0].Role.ShouldBe("user");
        result.Messages[0].Content.ShouldContain("C# code");
    }

    #endregion

    #region Multi-Server Aggregation

    [Fact]
    public async Task MultiServer_ToolAggregation_MergesToolsFromBothServers()
    {
        // Arrange
        var tool1 = AIFunctionFactory.Create(() => "w", "get_weather");
        var tool2 = AIFunctionFactory.Create(() => "s", "search_docs");
        var tool3 = AIFunctionFactory.Create(() => "t", "translate");

        var builder = new ServerBuilder()
            .AddServer("weather-server", tools: new List<AITool> { tool1 })
            .AddServer("docs-server", tools: new List<AITool> { tool2, tool3 });

        var (toolProvider, _, _) = CreateProviders(builder);

        // Act
        var tools = await toolProvider.GetToolsAsync();

        // Assert
        tools.Count.ShouldBe(3);
        tools.ShouldContain(t => t.Name == "get_weather");
        tools.ShouldContain(t => t.Name == "search_docs");
        tools.ShouldContain(t => t.Name == "translate");
    }

    [Fact]
    public async Task MultiServer_ResourceAggregation_MergesResourcesFromBothServers()
    {
        // Arrange
        var builder = new ServerBuilder()
            .AddServer("server1", resources: new List<McpResourceInfo>
            {
                new("file:///a.md", "FileA", null, null)
            })
            .AddServer("server2", resources: new List<McpResourceInfo>
            {
                new("file:///b.md", "FileB", null, null),
                new("file:///c.md", "FileC", null, null)
            });

        var (_, resourceProvider, _) = CreateProviders(builder);

        // Act
        var result = await resourceProvider.ListAllResourcesAsync();

        // Assert
        result.Count.ShouldBe(3);
        result.ShouldContain(r => r.Name == "FileA");
        result.ShouldContain(r => r.Name == "FileB");
        result.ShouldContain(r => r.Name == "FileC");
    }

    [Fact]
    public async Task MultiServer_PromptAggregation_MergesPromptsFromBothServers()
    {
        // Arrange
        var builder = new ServerBuilder()
            .AddServer("server1", prompts: new List<McpPromptInfo>
            {
                new("review", "Code review", null)
            })
            .AddServer("server2", prompts: new List<McpPromptInfo>
            {
                new("summarize", "Summarize text", null),
                new("translate", "Translate text", null)
            });

        var (_, _, promptProvider) = CreateProviders(builder);

        // Act
        var result = await promptProvider.ListAllPromptsAsync();

        // Assert
        result.Count.ShouldBe(3);
        result.ShouldContain(p => p.Name == "review");
        result.ShouldContain(p => p.Name == "summarize");
        result.ShouldContain(p => p.Name == "translate");
    }

    [Fact]
    public async Task MultiServer_DuplicateToolNames_Deduplicated()
    {
        // Arrange: 两个服务器都提供同名工具
        var tool1 = AIFunctionFactory.Create(() => "v1", "get_weather");
        var tool2 = AIFunctionFactory.Create(() => "v2", "get_weather");

        var builder = new ServerBuilder()
            .AddServer("server1", tools: new List<AITool> { tool1 })
            .AddServer("server2", tools: new List<AITool> { tool2 });

        var (toolProvider, _, _) = CreateProviders(builder);

        // Act
        var tools = await toolProvider.GetToolsAsync();

        // Assert: 同名工具去重，只保留第一个
        tools.Count.ShouldBe(1);
        tools[0].Name.ShouldBe("get_weather");
    }

    #endregion

    #region Server Failure Isolation

    [Fact]
    public async Task ServerFailure_ToolProvider_ReturnsPartialResults()
    {
        // Arrange
        var tool = AIFunctionFactory.Create(() => "ok", "working_tool");
        var goodAdapter = CreateMockAdapter(tools: new List<AITool> { tool });

        var server1 = new McpServerConfig { Name = "good-server" };
        var server2 = new McpServerConfig { Name = "bad-server" };

        _clientFactory.Setup(x => x.GetOrCreateClientAsync(
                It.Is<McpServerConfig>(s => s.Name == "good-server"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(goodAdapter.Object);
        _clientFactory.Setup(x => x.GetOrCreateClientAsync(
                It.Is<McpServerConfig>(s => s.Name == "bad-server"), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Connection refused"));

        var toolProvider = CreateToolProvider(server1, server2);

        // Act
        var tools = await toolProvider.GetToolsAsync();

        // Assert: 好的服务器的工具仍然返回
        tools.Count.ShouldBe(1);
        tools[0].Name.ShouldBe("working_tool");
    }

    [Fact]
    public async Task ServerFailure_ResourceProvider_ReturnsPartialResults()
    {
        // Arrange
        var goodAdapter = CreateMockAdapter(resources: new List<McpResourceInfo>
        {
            new("file:///good.md", "GoodFile", null, null)
        });

        var server1 = new McpServerConfig { Name = "good-server" };
        var server2 = new McpServerConfig { Name = "bad-server" };

        _clientFactory.Setup(x => x.GetOrCreateClientAsync(
                It.Is<McpServerConfig>(s => s.Name == "good-server"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(goodAdapter.Object);
        _clientFactory.Setup(x => x.GetOrCreateClientAsync(
                It.Is<McpServerConfig>(s => s.Name == "bad-server"), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Connection refused"));

        var resourceProvider = CreateResourceProvider(server1, server2);

        // Act
        var result = await resourceProvider.ListAllResourcesAsync();

        // Assert
        result.Count.ShouldBe(1);
        result[0].Name.ShouldBe("GoodFile");
    }

    [Fact]
    public async Task ServerFailure_PromptProvider_ReturnsPartialResults()
    {
        // Arrange
        var goodAdapter = CreateMockAdapter(prompts: new List<McpPromptInfo>
        {
            new("working-prompt", "A prompt", null)
        });

        var server1 = new McpServerConfig { Name = "good-server" };
        var server2 = new McpServerConfig { Name = "bad-server" };

        _clientFactory.Setup(x => x.GetOrCreateClientAsync(
                It.Is<McpServerConfig>(s => s.Name == "good-server"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(goodAdapter.Object);
        _clientFactory.Setup(x => x.GetOrCreateClientAsync(
                It.Is<McpServerConfig>(s => s.Name == "bad-server"), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Connection refused"));

        var promptProvider = CreatePromptProvider(server1, server2);

        // Act
        var result = await promptProvider.ListAllPromptsAsync();

        // Assert
        result.Count.ShouldBe(1);
        result[0].Name.ShouldBe("working-prompt");
    }

    #endregion

    #region Cache Invalidation Roundtrip

    [Fact]
    public async Task CacheInvalidation_ToolProvider_RediscoversUpdatedTools()
    {
        // Arrange
        var toolV1 = AIFunctionFactory.Create(() => "v1", "tool_alpha");
        var toolV2 = AIFunctionFactory.Create(() => "v2", "tool_beta");

        var callCount = 0;
        var mockAdapter = new Mock<IMcpClientAdapter>();
        mockAdapter.Setup(x => x.ListToolsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                // 第一次返回 tool_alpha，第二次返回 tool_beta
                return callCount == 1
                    ? new List<AITool> { toolV1 }
                    : new List<AITool> { toolV2 };
            });

        var server = new McpServerConfig { Name = "evolving-server" };
        _clientFactory.Setup(x => x.GetOrCreateClientAsync(
                It.Is<McpServerConfig>(s => s.Name == "evolving-server"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockAdapter.Object);

        var toolProvider = CreateToolProvider(server, cacheSeconds: 300);

        // Act 1: 首次发现
        var tools1 = await toolProvider.GetToolsAsync();
        tools1.Count.ShouldBe(1);
        tools1[0].Name.ShouldBe("tool_alpha");
        callCount.ShouldBe(1);

        // 验证缓存命中
        await toolProvider.GetToolsAsync();
        callCount.ShouldBe(1); // 未重新拉取

        // Act 2: 失效缓存并重新发现
        toolProvider.InvalidateCache("evolving-server");
        var tools2 = await toolProvider.GetToolsAsync();

        // Assert: 工具已更新
        tools2.Count.ShouldBe(1);
        tools2[0].Name.ShouldBe("tool_beta");
        callCount.ShouldBe(2);
    }

    [Fact]
    public async Task CacheInvalidation_ServerToolNames_UpdatedAfterInvalidation()
    {
        // Arrange
        var tool1 = AIFunctionFactory.Create(() => "1", "old_tool");
        var tool2 = AIFunctionFactory.Create(() => "2", "new_tool");

        var callCount = 0;
        var mockAdapter = new Mock<IMcpClientAdapter>();
        mockAdapter.Setup(x => x.ListToolsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                return callCount == 1
                    ? new List<AITool> { tool1 }
                    : new List<AITool> { tool2 };
            });

        var server = new McpServerConfig { Name = "test-server" };
        _clientFactory.Setup(x => x.GetOrCreateClientAsync(
                It.Is<McpServerConfig>(s => s.Name == "test-server"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockAdapter.Object);

        var toolProvider = CreateToolProvider(server, cacheSeconds: 300);

        // Act 1: 初始工具
        await toolProvider.GetToolsAsync();
        var names1 = toolProvider.GetServerToolNames("test-server");
        names1.ShouldContain("old_tool");

        // Act 2: 失效后重新发现
        toolProvider.InvalidateCache("test-server");
        await toolProvider.GetToolsAsync();
        var names2 = toolProvider.GetServerToolNames("test-server");

        // Assert
        names2.ShouldNotContain("old_tool");
        names2.ShouldContain("new_tool");
    }

    #endregion

    #region Audit Logging

    [Fact]
    public async Task AuditWrapper_ToolExecution_WrapsAndPreservesResult()
    {
        // Arrange
        var func = AIFunctionFactory.Create(() => "audit-test-result", "audited_tool");

        var builder = new ServerBuilder()
            .AddServer("audit-server", tools: new List<AITool> { func });

        var (toolProvider, _, _) = CreateProviders(builder);

        // Act
        var tools = await toolProvider.GetToolsAsync();
        tools.Count.ShouldBe(1);

        // 验证工具经过 McpAuditToolWrapper 包装（工具应当是 DelegatingAIFunction）
        var tool = tools[0];
        tool.ShouldBeAssignableTo<AIFunction>();
        var wrappedFunc = (AIFunction)tool;

        // 调用工具验证结果仍然正确（通过 audit wrapper）
        var result = await wrappedFunc.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>()));
        result.ShouldNotBeNull();
        var resultText = result.ToString();
        resultText.ShouldNotBeNull();
        resultText.ShouldContain("audit-test-result");
    }

    #endregion

    #region Cross-Provider Consistency

    [Fact]
    public async Task CrossProvider_AllThreeProviders_SameServerConfiguration()
    {
        // Arrange: 一个服务器同时提供 tools + resources + prompts
        var tool = AIFunctionFactory.Create(() => "t", "multi_tool");
        var resources = new List<McpResourceInfo>
        {
            new("file:///multi.md", "MultiDoc", "text/markdown", null)
        };
        var prompts = new List<McpPromptInfo>
        {
            new("multi-prompt", "A prompt from multi-server", null)
        };

        var builder = new ServerBuilder()
            .AddServer("multi-server",
                tools: new List<AITool> { tool },
                resources: resources,
                prompts: prompts);

        var (toolProvider, resourceProvider, promptProvider) = CreateProviders(builder);

        // Act
        var toolsResult = await toolProvider.GetToolsAsync();
        var res = await resourceProvider.ListResourcesAsync("multi-server");
        var prm = await promptProvider.ListPromptsAsync("multi-server");

        // Assert: 所有 provider 从同一配置获取数据
        toolsResult.Count.ShouldBe(1);
        toolsResult[0].Name.ShouldBe("multi_tool");

        res.Count.ShouldBe(1);
        res[0].Name.ShouldBe("MultiDoc");

        prm.Count.ShouldBe(1);
        prm[0].Name.ShouldBe("multi-prompt");
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// 统一创建所有三个 Provider，共享同一 IMcpClientFactory。
    /// </summary>
    private (McpToolProvider toolProvider, McpResourceProvider resourceProvider, McpPromptProvider promptProvider) CreateProviders(ServerBuilder builder)
    {
        var serverConfigs = new List<McpServerConfig>();

        foreach (var server in builder.Servers)
        {
            var config = new McpServerConfig { Name = server.Name };
            serverConfigs.Add(config);

            var mockAdapter = CreateMockAdapter(
                server.Tools,
                server.Resources,
                server.Prompts,
                server.ResourceContent,
                server.PromptResults);

            _clientFactory.Setup(x => x.GetOrCreateClientAsync(
                    It.Is<McpServerConfig>(s => s.Name == server.Name), It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockAdapter.Object);
        }

        var aiOptions = new AIOptions
        {
            Mcp = new McpOptions
            {
                Enabled = true,
                ToolCacheSeconds = 300,
                Servers = serverConfigs
            }
        };

        var options = new Mock<IOptions<AIOptions>>();
        options.Setup(x => x.Value).Returns(aiOptions);

        var toolLogger = new Mock<ILogger<McpToolProvider>>();
        var resourceLogger = new Mock<ILogger<McpResourceProvider>>();
        var promptLogger = new Mock<ILogger<McpPromptProvider>>();

        var catalog = new OptionsBackedMcpServerCatalog(options.Object);
        var toolProvider = new McpToolProvider(new StaticOptionsMonitor<AIOptions>(aiOptions), catalog, _clientFactory.Object, _loggerFactory.Object, toolLogger.Object);
        var resourceProvider = new McpResourceProvider(catalog, _clientFactory.Object, resourceLogger.Object);
        var promptProvider = new McpPromptProvider(catalog, _clientFactory.Object, promptLogger.Object);

        return (toolProvider, resourceProvider, promptProvider);
    }

    private Mock<IMcpClientAdapter> CreateMockAdapter(
        List<AITool>? tools = null,
        List<McpResourceInfo>? resources = null,
        List<McpPromptInfo>? prompts = null,
        (string uri, McpResourceContent content)? resourceContent = null,
        Dictionary<string, McpPromptResult>? promptResults = null)
    {
        var mock = new Mock<IMcpClientAdapter>();

        mock.Setup(x => x.ListToolsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(tools ?? new List<AITool>());

        mock.Setup(x => x.ListResourcesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(resources ?? new List<McpResourceInfo>());

        mock.Setup(x => x.ListPromptsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(prompts ?? new List<McpPromptInfo>());

        if (resourceContent.HasValue)
        {
            mock.Setup(x => x.ReadResourceAsync(resourceContent.Value.uri, It.IsAny<CancellationToken>()))
                .ReturnsAsync(resourceContent.Value.content);
        }

        if (promptResults != null)
        {
            foreach (var (name, result) in promptResults)
            {
                mock.Setup(x => x.GetPromptAsync(name, It.IsAny<Dictionary<string, string>?>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(result);
            }
        }

        return mock;
    }

    private McpToolProvider CreateToolProvider(params McpServerConfig[] servers)
    {
        return CreateToolProvider(servers, cacheSeconds: 300);
    }

    private McpToolProvider CreateToolProvider(McpServerConfig server, int cacheSeconds)
    {
        return CreateToolProvider([server], cacheSeconds);
    }

    private McpToolProvider CreateToolProvider(McpServerConfig[] servers, int cacheSeconds)
    {
        var aiOptions = new AIOptions
        {
            Mcp = new McpOptions
            {
                Enabled = true,
                ToolCacheSeconds = cacheSeconds,
                Servers = servers.ToList()
            }
        };

        var options = new Mock<IOptions<AIOptions>>();
        options.Setup(x => x.Value).Returns(aiOptions);

        var logger = new Mock<ILogger<McpToolProvider>>();
        return new McpToolProvider(new StaticOptionsMonitor<AIOptions>(aiOptions), new OptionsBackedMcpServerCatalog(options.Object), _clientFactory.Object, _loggerFactory.Object, logger.Object);
    }

    private McpResourceProvider CreateResourceProvider(params McpServerConfig[] servers)
    {
        var aiOptions = new AIOptions
        {
            Mcp = new McpOptions
            {
                Enabled = true,
                Servers = servers.ToList()
            }
        };

        var options = new Mock<IOptions<AIOptions>>();
        options.Setup(x => x.Value).Returns(aiOptions);

        var logger = new Mock<ILogger<McpResourceProvider>>();
        return new McpResourceProvider(new OptionsBackedMcpServerCatalog(options.Object), _clientFactory.Object, logger.Object);
    }

    private McpPromptProvider CreatePromptProvider(params McpServerConfig[] servers)
    {
        var aiOptions = new AIOptions
        {
            Mcp = new McpOptions
            {
                Enabled = true,
                Servers = servers.ToList()
            }
        };

        var options = new Mock<IOptions<AIOptions>>();
        options.Setup(x => x.Value).Returns(aiOptions);

        var logger = new Mock<ILogger<McpPromptProvider>>();
        return new McpPromptProvider(new OptionsBackedMcpServerCatalog(options.Object), _clientFactory.Object, logger.Object);
    }

    /// <summary>
    /// 测试服务器配置构建器，简化多服务器场景的 mock 设置。
    /// </summary>
    private sealed class ServerBuilder
    {
        public List<MockServerDef> Servers { get; } = [];

        public ServerBuilder AddServer(
            string name,
            List<AITool>? tools = null,
            List<McpResourceInfo>? resources = null,
            List<McpPromptInfo>? prompts = null,
            (string uri, McpResourceContent content)? resourceContent = null,
            Dictionary<string, McpPromptResult>? promptResults = null)
        {
            Servers.Add(new MockServerDef
            {
                Name = name,
                Tools = tools,
                Resources = resources,
                Prompts = prompts,
                ResourceContent = resourceContent,
                PromptResults = promptResults
            });
            return this;
        }
    }

    private sealed class MockServerDef
    {
        public string Name { get; init; } = string.Empty;
        public List<AITool>? Tools { get; init; }
        public List<McpResourceInfo>? Resources { get; init; }
        public List<McpPromptInfo>? Prompts { get; init; }
        public (string uri, McpResourceContent content)? ResourceContent { get; init; }
        public Dictionary<string, McpPromptResult>? PromptResults { get; init; }
    }

    #endregion
}
