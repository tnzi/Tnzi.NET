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
[ConfigSection("AI:Rag:HybridSearch")]
[RuntimeSettingGroup(Key = "ai-rag", Module = "AI", DisplayName = "RAG",
    I18nKey = "admin.modules.system.settings.groups.aiRag", Icon = "mdi:book-search-outline", Order = 155)]
public class HybridSearchOptions
{
    /// <summary>
    /// 是否启用混合搜索（默认 false，仅使用向量搜索）
    /// <para>
    /// 装配门（KEEP-STATIC）：<see cref="AIRagModule"/> 据此在 <c>ConfigureServicesAsync</c> 决定注册
    /// <c>HybridSearchService</c> 还是 <c>VectorTextSearchService</c>，属 DI 装配开关，不可热更新。
    /// </para>
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// 向量搜索结果的权重（默认 0.7）
    /// </summary>
    [RuntimeSetting(Label = "Vector Weight", I18n = "admin.modules.system.settings.fields.ragHybridVectorWeight",
        Type = SettingFieldType.Decimal, Min = 0, Max = 1, Subsection = "Hybrid Search",
        Description = "Weight applied to vector-search results during fusion")]
    public double VectorWeight { get; set; } = 0.7;

    /// <summary>
    /// 关键词搜索结果的权重（默认 0.3）
    /// </summary>
    [RuntimeSetting(Label = "Keyword Weight", I18n = "admin.modules.system.settings.fields.ragHybridKeywordWeight",
        Type = SettingFieldType.Decimal, Min = 0, Max = 1, Subsection = "Hybrid Search",
        Description = "Weight applied to keyword-search results during fusion")]
    public double KeywordWeight { get; set; } = 0.3;

    /// <summary>
    /// RRF 融合常数 K — 值越大，排名差异对得分的影响越小（默认 60）
    /// </summary>
    [RuntimeSetting(Label = "Fusion Constant K", I18n = "admin.modules.system.settings.fields.ragHybridFusionConstantK",
        Type = SettingFieldType.Int, Min = 1, Max = 1000, Subsection = "Hybrid Search",
        Description = "RRF fusion constant; larger values reduce the impact of rank differences")]
    public int FusionConstantK { get; set; } = 60;
}
