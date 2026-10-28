namespace Tnzi.AI.Workflow.Engine;

/// <summary>
/// 条件边 - 根据节点输出动态路由到不同的下游节点
/// </summary>
/// <remarks>
/// 当 <see cref="ConditionType"/> 为 <see cref="EdgeConditionType.OutputContains"/> 时，
/// 检查节点输出是否包含 Routes 字典中的 Key，匹配则路由到对应 Value 指定的节点。
/// 未匹配任何路由时使用 <see cref="DefaultTarget"/>（如果设置）。
/// </remarks>
public class ConditionalEdge
{
    private readonly Dictionary<string, string> _routes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>源节点 ID</summary>
    public required string FromNodeId { get; init; }

    /// <summary>路由映射（Key: 匹配条件, Value: 目标节点 ID）。键不区分大小写。</summary>
    /// <remarks>
    /// ★ 比较器在这里定，不靠构造方：字典的比较器不随 JSON 序列化，存储的定义读回来就是区分大小写的字典，
    /// 而 review / conditional 节点给出的路由键一律小写。键写成 <c>Approve</c> 时每个裁决都查不到、静默落到
    /// <see cref="DefaultTarget"/>，审查者要求返工的内容照样被发布。只差大小写的两个键无法区分，构造时即拒绝。
    /// </remarks>
    public required Dictionary<string, string> Routes
    {
        get => _routes;
        init
        {
            Check.NotNull(value);
            var routes = new Dictionary<string, string>(value.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var (key, target) in value)
            {
                if (!routes.TryAdd(key, target))
                    throw new ArgumentException($"Conditional edge route keys are case-insensitive; '{key}' is given more than once.", nameof(Routes));
            }

            _routes = routes;
        }
    }

    /// <summary>默认目标节点 ID（无匹配路由时使用）</summary>
    public string? DefaultTarget { get; init; }

    /// <summary>条件评估类型</summary>
    public EdgeConditionType ConditionType { get; init; } = EdgeConditionType.OutputContains;
}

/// <summary>
/// 条件边评估类型
/// </summary>
public enum EdgeConditionType
{
    /// <summary>输出文本包含指定关键词</summary>
    OutputContains,

    /// <summary>输出文本精确等于指定值</summary>
    OutputEquals,

    /// <summary>通过 JsonPath 提取值匹配</summary>
    JsonPath
}
