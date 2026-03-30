namespace Tnzi.AI.Rag.Options;

/// <summary>
/// 加权递减重排序器配置选项
/// <para>
/// 灵感来自 Kernel Memory v2 的三阶段重排序策略：
/// Phase 1: 按知识库权重调整得分
/// Phase 2: 按内容哈希分组，递减聚合（同一内容出现在多个块中时获得额外加分）
/// Phase 3: 按得分降序排列
/// </para>
/// </summary>
public class RerankerOptions
{
    /// <summary>
    /// 递减因子（0-1）— 同一内容的后续匹配贡献递减
    /// <para>
    /// 第 i 次匹配的贡献 = score × diminishingFactor^i。
    /// 值越小，重复匹配的额外加分越少。
    /// </para>
    /// </summary>
    public double DiminishingFactor { get; set; } = 0.5;

    /// <summary>
    /// 合并后得分上限
    /// </summary>
    public double MaxMergedScore { get; set; } = 1.0;

    /// <summary>
    /// 知识库权重映射（KnowledgeBaseId → 权重）
    /// <para>
    /// 未配置的知识库默认权重为 1.0。
    /// 示例：{"高优先级知识库ID": 1.5, "低优先级知识库ID": 0.5}
    /// </para>
    /// </summary>
    public Dictionary<string, double> KnowledgeBaseWeights { get; set; } = new();
}
