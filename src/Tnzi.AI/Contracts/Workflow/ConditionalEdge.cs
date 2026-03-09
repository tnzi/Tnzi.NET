namespace Tnzi.AI.Contracts.Workflow;

/// <summary>
/// 条件边 — 根据节点输出动态路由到不同的下游节点
/// </summary>
/// <remarks>
/// 当 <see cref="ConditionType"/> 为 <see cref="EdgeConditionType.OutputContains"/> 时，
/// 检查节点输出是否包含 Routes 字典中的 Key，匹配则路由到对应 Value 指定的节点。
/// 未匹配任何路由时使用 <see cref="DefaultTarget"/>（如果设置）。
/// </remarks>
public class ConditionalEdge
{
    /// <summary>源节点 ID</summary>
    public required string FromNodeId { get; init; }

    /// <summary>路由映射（Key: 匹配条件, Value: 目标节点 ID）</summary>
    public required Dictionary<string, string> Routes { get; init; }

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
    JsonPath,

    /// <summary>通过 LLM 对输出进行分类</summary>
    LlmClassify
}
