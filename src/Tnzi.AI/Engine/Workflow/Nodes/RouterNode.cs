namespace Tnzi.AI.Engine.Workflow.Nodes;

/// <summary>
/// 路由节点 — 输入 + 候选路由列表 → LLM 分类 → 返回 RouteTo
/// </summary>
/// <remarks>
/// 配置项：
/// - routerAgentId: 路由器 Agent ID（可选）
/// - routes: JSON 字典，格式 {"routeKey": "描述"}，如 {"technical": "技术问题", "billing": "账单问题"}
/// </remarks>
public class RouterNode : IWorkflowNode
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RouterNode> _logger;

    public string NodeType => "router";

    public RouterNode(IServiceProvider serviceProvider, ILogger<RouterNode> logger)
    {
        _serviceProvider = Check.NotNull(serviceProvider);
        _logger = Check.NotNull(logger);
    }

    public async Task<WorkflowNodeResult> ExecuteAsync(WorkflowNodeContext context, CancellationToken cancellationToken = default)
    {
        var step = context.Step;
        var state = context.State;
        var config = step.Configuration ?? new Dictionary<string, string>();

        // 解析路由选项
        var routes = ParseRoutes(config);
        if (routes.Count == 0)
        {
            _logger.LogWarning("Router node '{StepId}' has no routes configured", step.StepId);
            return new WorkflowNodeResult
            {
                Output = "No routes configured",
                IsSuccess = false,
                Error = "Router node requires 'routes' configuration"
            };
        }

        // 构建输入
        var inputText = CollectInput(context);
        inputText = state.ResolveTemplate(inputText);

        // 构建路由提示
        var routerPrompt = BuildRouterPrompt(step.Instructions, inputText, routes);

        // 创建作用域以避免使用根 ServiceProvider 解析 Scoped 服务
        using var scope = _serviceProvider.CreateScope();
        var agentFactory = scope.ServiceProvider.GetRequiredService<IAgentFactory>();

        string? provider = step.Provider;
        string? model = step.Model;

        if (config.TryGetValue("routerAgentId", out var routerIdStr)
            && Guid.TryParse(routerIdStr, out var routerAgentId))
        {
            var agentRepo = scope.ServiceProvider.GetService<IRepository<Agent, Guid>>();
            if (agentRepo != null)
            {
                var routerAgent = await agentRepo.GetAsync(routerAgentId, cancellationToken);
                if (routerAgent != null)
                {
                    provider ??= routerAgent.Provider;
                    model ??= routerAgent.Model;
                }
            }
        }

        var executor = await agentFactory.CreateAgentAsync(
            providerName: provider,
            model: model,
            instructions: "You are a router. Classify the input and respond with ONLY the route key, nothing else.",
            name: step.StepId ?? "router-node",
            ct: cancellationToken);

        var messages = new List<ChatMessage> { new(ChatRole.User, routerPrompt) };
        var response = await executor.ExecuteAsync(messages, cancellationToken);

        // 解析路由结果
        var (selectedRoute, isFallback) = ParseRouteResult(response.Text ?? string.Empty, routes);

        if (isFallback)
        {
            _logger.LogWarning("Router node '{StepId}' could not match LLM response, falling back to '{Route}'", step.StepId, selectedRoute);
        }
        else
        {
            _logger.LogDebug("Router node '{StepId}' selected route: {Route}", step.StepId, selectedRoute);
        }

        return new WorkflowNodeResult
        {
            Output = new WorkflowStepOutput
            {
                Text = inputText,
                Metadata = new Dictionary<string, string>
                {
                    ["route"] = selectedRoute
                }
            },
            Usage = response.Usage,
            IsSuccess = true,
            RouteTo = selectedRoute
        };
    }

    /// <summary>
    /// 从配置中解析路由字典
    /// </summary>
    private static Dictionary<string, string> ParseRoutes(Dictionary<string, string> config)
    {
        if (!config.TryGetValue("routes", out var routesJson) || string.IsNullOrWhiteSpace(routesJson))
            return new Dictionary<string, string>();

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(routesJson, TnziJsonDefaults.Options)
                   ?? new Dictionary<string, string>();
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }

    /// <summary>
    /// 收集输入（上游输出或初始输入）
    /// </summary>
    private static string CollectInput(WorkflowNodeContext context)
    {
        if (context.DependencyOutputs.Count == 0)
            return context.State.InitialInput;

        if (context.DependencyOutputs.Count == 1)
            return context.DependencyOutputs.Values.First().Text;

        var sb = new StringBuilder();
        foreach (var (depId, output) in context.DependencyOutputs)
        {
            if (sb.Length > 0) sb.AppendLine();
            sb.AppendLine($"[{depId}]");
            sb.AppendLine(output.Text);
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// 构建路由提示
    /// </summary>
    private static string BuildRouterPrompt(string? customInstructions, string inputText, Dictionary<string, string> routes)
    {
        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(customInstructions))
        {
            sb.AppendLine(customInstructions);
            sb.AppendLine();
        }

        sb.AppendLine("Classify the following input into one of these categories:");
        sb.AppendLine();
        foreach (var (key, description) in routes)
        {
            sb.AppendLine($"- {key}: {description}");
        }

        sb.AppendLine();
        sb.AppendLine($"Respond with ONLY the category key (one of: {string.Join(", ", routes.Keys)}).");
        sb.AppendLine();
        sb.AppendLine("--- Input ---");
        sb.AppendLine(inputText);

        return sb.ToString();
    }

    /// <summary>
    /// 解析 LLM 返回的路由选择
    /// </summary>
    /// <returns>选中的路由 key 和是否为 fallback</returns>
    private static (string route, bool isFallback) ParseRouteResult(string responseText, Dictionary<string, string> routes)
    {
        var trimmed = responseText.Trim();

        // 精确匹配
        foreach (var key in routes.Keys)
        {
            if (string.Equals(trimmed, key, StringComparison.OrdinalIgnoreCase))
                return (key, false);
        }

        // 包含匹配
        var lowerText = trimmed.ToLowerInvariant();
        foreach (var key in routes.Keys)
        {
            if (lowerText.Contains(key.ToLowerInvariant()))
                return (key, false);
        }

        // 默认 fallback 到第一个路由
        return (routes.Keys.First(), true);
    }
}
