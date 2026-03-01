namespace Tnzi.AI.Rag.Options;

/// <summary>
/// 混合搜索配置选项
/// </summary>
/// <remarks>
/// <para>
/// 控制 <see cref="Search.HybridSearchService"/> 的行为参数。
/// 通过 <c>AI:Rag:HybridSearch</c> 配置节绑定。
/// </para>
/// </remarks>
[ExperimentalApi(Reason = "Hybrid search options are in preview")]
public class HybridSearchOptions
{
    /// <summary>
    /// 是否启用混合搜索（默认 false，仅使用向量搜索）
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// 向量搜索结果的权重（默认 0.7）
    /// </summary>
    public double VectorWeight { get; set; } = 0.7;

    /// <summary>
    /// 关键词搜索结果的权重（默认 0.3）
    /// </summary>
    public double KeywordWeight { get; set; } = 0.3;

    /// <summary>
    /// RRF 融合常数 K — 值越大，排名差异对得分的影响越小（默认 60）
    /// </summary>
    public int FusionConstantK { get; set; } = 60;
}
