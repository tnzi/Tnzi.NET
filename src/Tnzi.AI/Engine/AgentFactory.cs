
namespace Tnzi.AI.Engine;

/// <summary>
/// Agent 工厂 — 编排 ChatClient 获取、工具解析和 Options 构建来创建 AgentExecutor
/// </summary>
public class AgentFactory : IAgentFactory
{
    private readonly IChatClientFactory _chatClientFactory;
    private readonly IOptionsMonitor<AIOptions> _options;
    private readonly IToolResolver _toolResolver;
    private readonly AgentExecutorOptionsBuilder _optionsBuilder;
    private readonly IToolRegistry _toolRegistry;
    private readonly ILogger<AgentFactory> _logger;

    public AgentFactory(
        IChatClientFactory chatClientFactory,
        IOptionsMonitor<AIOptions> options,
        IToolResolver toolResolver,
        AgentExecutorOptionsBuilder optionsBuilder,
        IToolRegistry toolRegistry,
        ILogger<AgentFactory> logger)
    {
        _chatClientFactory = Check.NotNull(chatClientFactory);
        _options = Check.NotNull(options);
        _toolResolver = Check.NotNull(toolResolver);
        _optionsBuilder = Check.NotNull(optionsBuilder);
        _toolRegistry = Check.NotNull(toolRegistry);
        _logger = Check.NotNull(logger);
    }

    /// <inheritdoc />
    public async Task<IAgentExecutor> CreateAgentAsync(
        string? providerName = null,
        string? model = null,
        string? instructions = null,
        string? name = null,
        IEnumerable<string>? toolGroups = null,
        double? temperature = null,
        int? maxTokens = null,
        AgentExecutorOptions? options = null,
        IEnumerable<string>? userPermissions = null,
        IEnumerable<string>? toolNames = null,
        Guid? agentId = null,
        CancellationToken ct = default)
    {
        // 1. 解析 Provider 配置
        var resolvedProvider = providerName ?? _options.CurrentValue.DefaultProvider;

        if (!_options.CurrentValue.Providers.TryGetValue(resolvedProvider, out var providerConfig))
        {
            throw new InvalidOperationException($"AI provider '{resolvedProvider}' is not configured");
        }

        if (!providerConfig.Enabled)
        {
            throw new InvalidOperationException($"AI provider '{resolvedProvider}' is disabled");
        }

        // 2. 获取 MEAI ChatClient
        var meaiChatClient = _chatClientFactory.GetChatClient(providerName, model);
        var resolvedModel = model ?? providerConfig.DefaultModel;

        // 3. 解析工具（C# + MCP 合并，按用户权限过滤；toolNames 提供 per-tool 授权/请求覆盖）
        var toolNamesList = toolNames as IReadOnlyCollection<string> ?? toolNames?.ToList();
        var hasToolNames = toolNamesList is { Count: > 0 };
        var tools = await _toolResolver.ResolveToolsAsync(toolGroups, userPermissions, toolNamesList, ct);

        // 4. 构建 AgentExecutorOptions
        var executorOptions = _optionsBuilder.Build(options, name, instructions, tools, temperature, maxTokens, agentId, agentName: name);

        // 5b. 从 IToolRegistry 填充 ToolDefinitions（供并行执行守卫和 GracefulShutdown 中断路径使用）
        // 仅当调用方指定了 toolGroups 或 toolNames 时才查询：MCP/OpenAPI 工具无注册表元数据，不在此填充，
        // executor 遇到未知工具时自动降级为顺序执行（fail-closed 安全默认）。
        if (toolGroups != null || hasToolNames)
        {
            var dict = new Dictionary<string, ToolDefinition>(StringComparer.OrdinalIgnoreCase);
            if (toolGroups != null)
            {
                foreach (var td in _toolRegistry.GetToolsByGroupsWithPermissions(toolGroups.ToList(), userPermissions))
                {
                    dict.TryAdd(td.Name, td);
                }
            }
            if (hasToolNames)
            {
                foreach (var td in _toolRegistry.GetToolsByNames(toolNamesList!, userPermissions))
                {
                    dict.TryAdd(td.Name, td);
                }
            }
            if (dict.Count > 0)
            {
                executorOptions.ToolDefinitions = dict;
            }
        }

        // 6. 创建 AgentExecutor（注入 Logger 以便记录工具执行异常）
        executorOptions.Logger = _logger;
        var agent = new AgentExecutor(meaiChatClient, executorOptions);

        _logger.LogDebug(
            "AgentExecutor created: Name={Name}, Provider={Provider}, Model={Model}, ToolCount={ToolCount}",
            executorOptions.Name, resolvedProvider, resolvedModel, tools?.Count ?? 0);

        return agent;
    }
}
