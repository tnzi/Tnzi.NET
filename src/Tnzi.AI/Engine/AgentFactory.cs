
namespace Tnzi.AI.Engine;

/// <summary>
/// Agent 工厂 — 编排 ChatClient 获取、工具解析和 Options 构建来创建 AgentExecutor
/// </summary>
public class AgentFactory : IAgentFactory
{
    private readonly IChatClientFactory _chatClientFactory;
    private readonly IOptions<AIOptions> _options;
    private readonly IToolResolver _toolResolver;
    private readonly AgentExecutorOptionsBuilder _optionsBuilder;
    private readonly ILogger<AgentFactory> _logger;

    public AgentFactory(
        IChatClientFactory chatClientFactory,
        IOptions<AIOptions> options,
        IToolResolver toolResolver,
        AgentExecutorOptionsBuilder optionsBuilder,
        ILogger<AgentFactory> logger)
    {
        _chatClientFactory = Check.NotNull(chatClientFactory);
        _options = Check.NotNull(options);
        _toolResolver = Check.NotNull(toolResolver);
        _optionsBuilder = Check.NotNull(optionsBuilder);
        _logger = Check.NotNull(logger);
    }

    /// <inheritdoc />
    public async Task<AgentExecutor> CreateAgentAsync(
        string? providerName = null,
        string? model = null,
        string? instructions = null,
        string? name = null,
        IEnumerable<string>? toolGroups = null,
        double? temperature = null,
        int? maxTokens = null,
        AgentExecutorOptions? options = null,
        IEnumerable<string>? userPermissions = null,
        Guid? agentId = null,
        CancellationToken ct = default)
    {
        // 1. 解析 Provider 配置
        var resolvedProvider = providerName ?? _options.Value.DefaultProvider;

        if (!_options.Value.Providers.TryGetValue(resolvedProvider, out var providerConfig))
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

        // 3. 解析工具（C# + MCP 合并，按用户权限过滤）
        var tools = await _toolResolver.ResolveToolsAsync(toolGroups, userPermissions, ct);

        // 4. 构建 AgentExecutorOptions
        var executorOptions = _optionsBuilder.Build(options, name, instructions, tools, temperature, maxTokens, agentId, agentName: name);

        // 5. 创建 AgentExecutor（注入 Logger 以便记录工具执行异常）
        executorOptions.Logger = _logger;
        var agent = new AgentExecutor(meaiChatClient, executorOptions);

        _logger.LogDebug(
            "AgentExecutor created: Name={Name}, Provider={Provider}, Model={Model}, ToolCount={ToolCount}",
            executorOptions.Name, resolvedProvider, resolvedModel, tools?.Count ?? 0);

        return agent;
    }
}
