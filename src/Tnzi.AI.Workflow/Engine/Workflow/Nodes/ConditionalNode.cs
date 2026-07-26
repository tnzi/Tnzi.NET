namespace Tnzi.AI.Workflow.Engine.Nodes;

/// <summary>
/// 条件节点 - 评估条件表达式 → 返回 RouteTo 进行条件边路由
/// </summary>
/// <remarks>
/// 配置项：
/// - condition: 条件表达式，支持以下格式：
///   - 简单值匹配：直接输出匹配值（如 "accept"）
///   - 模板变量：{{stepId}} 引用前置步骤输出（仅支持整步输出，不支持 {{stepId.field}} 这类字段路径）
///   - 默认路由：当条件无法评估时使用 "default" 路由
/// 上游若带 verdict / route 元数据（ReviewNode / RouterNode 的输出），优先取其作为路由键，
/// 此时 condition 只用于生成节点输出文本。
/// </remarks>
public class ConditionalNode : IWorkflowNode
{
    private readonly ILogger<ConditionalNode> _logger;

    public string NodeType => WorkflowNodeTypes.Conditional;

    public ConditionalNode(ILogger<ConditionalNode> logger)
    {
        _logger = Check.NotNull(logger);
    }

    public Task<WorkflowNodeResult> ExecuteAsync(WorkflowNodeContext context, CancellationToken cancellationToken = default)
    {
        var step = context.Step;
        var state = context.State;
        var config = step.Configuration ?? new Dictionary<string, string>();

        // 获取条件表达式
        var condition = config.TryGetValue("condition", out var cond) ? cond : step.Condition;

        if (string.IsNullOrWhiteSpace(condition))
        {
            _logger.LogWarning("Conditional node '{StepId}' has no condition configured", step.StepId);
            return Task.FromResult(new WorkflowNodeResult
            {
                Output = new WorkflowStepOutput
                {
                    Text = string.Empty,
                    Metadata = new Dictionary<string, string> { ["route"] = "default" }
                },
                IsSuccess = true,
                RouteTo = "default"
            });
        }

        // 解析模板变量
        var resolvedCondition = state.ResolveTemplate(condition);

        // 尝试从上游元数据中获取路由值
        var routeValue = ResolveRouteValue(resolvedCondition, context);

        _logger.LogDebug("Conditional node '{StepId}' evaluated to route: {Route}", step.StepId, routeValue);

        return Task.FromResult(new WorkflowNodeResult
        {
            Output = new WorkflowStepOutput
            {
                Text = resolvedCondition,
                Metadata = new Dictionary<string, string> { ["route"] = routeValue }
            },
            IsSuccess = true,
            RouteTo = routeValue
        });
    }

    /// <summary>
    /// 解析条件表达式，返回 ConditionalNode 的**自由值路由键**（free-value routing）。
    /// <para>
    /// 注意：这与 <see cref="WorkflowEngine"/> 的 <c>EvaluateCondition</c> 语义不同——
    /// 引擎层的 <c>EvaluateCondition</c> 是 <b>fail-closed 真值门控</b>（仅白名单
    /// <c>true/1/yes</c> 判真，其余判假跳过节点）；本方法是 <b>路由值解析</b>，把
    /// 上游 verdict/route 元数据或简单值原样作为下游条件边的路由键返回，无法识别时
    /// 回退到 <c>"default"</c>。两者职责不可互换，故命名刻意区分。
    /// </para>
    /// </summary>
    private static string ResolveRouteValue(string condition, WorkflowNodeContext context)
    {
        var trimmed = condition.Trim();

        // 检查上游依赖的 metadata 中是否有 verdict/route 字段
        foreach (var (_, output) in context.DependencyOutputs)
        {
            if (output.Metadata == null) continue;

            // 优先使用 verdict（ReviewNode 的输出）
            if (output.Metadata.TryGetValue("verdict", out var verdict))
                return verdict;

            // 其次使用 route（RouterNode 的输出）
            if (output.Metadata.TryGetValue("route", out var route))
                return route;
        }

        // 简单值：直接作为路由值返回
        if (!string.IsNullOrWhiteSpace(trimmed) && !trimmed.Contains(' '))
            return trimmed.ToLowerInvariant();

        // 布尔判断
        var lower = trimmed.ToLowerInvariant();
        if (lower is "true" or "1" or "yes") return "true";
        if (lower is "false" or "0" or "no") return "false";

        return "default";
    }
}
