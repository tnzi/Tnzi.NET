
namespace Tnzi.AI.Engine;

/// <summary>
/// Agent 工厂 - 编排 ChatClient 获取、工具解析和 Options 构建来创建 AgentExecutor
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
        // 1. 获取 MEAI ChatClient。提供商的解析与校验只在工厂里做一次：调用方租户的启用行 > System 启用行 >
        // 已启用的 AI:Providers 配置条目，名字不存在或已禁用时由工厂抛出。
        // ★ 这里不能再先查一遍 AI:Providers：管理端登记的提供商（Agent 编辑器的下拉框把它们列在最前面）只存在于库里，
        // 那道预检让绑定它们的 Agent 每次对话都失败；库里重新启用一个配置里 Enabled=false 的同名条目，也照样被拒。
        // 空白与未指定同义：省略 provider 的 YAML 定义在实体里存的是空串（列非空），它的意思是「用默认提供商」
        var requestedProvider = string.IsNullOrWhiteSpace(providerName) ? null : providerName;
        var resolvedProvider = requestedProvider ?? _options.CurrentValue.DefaultProvider;
        var meaiChatClient = _chatClientFactory.GetChatClient(requestedProvider, model);

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
            executorOptions.Name, resolvedProvider, model ?? "(provider default)", tools?.Count ?? 0);

        return agent;
    }
}
