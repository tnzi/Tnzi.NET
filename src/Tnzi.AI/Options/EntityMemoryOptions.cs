namespace Tnzi.AI.Options;

/// <summary>
/// 实体记忆配置选项
/// </summary>
public class EntityMemoryOptions
{
    /// <summary>
    /// 是否启用实体记忆上下文提供器
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// 每次上下文注入的最大实体数量
    /// </summary>
    public int MaxEntitiesPerContext { get; set; } = 30;

    /// <summary>
    /// 实体抽取使用的提供商名称（null 使用默认提供商）
    /// </summary>
    public string? ExtractionProvider { get; set; }

    /// <summary>
    /// 实体抽取使用的模型 ID（null 使用提供商默认模型）
    /// </summary>
    public string? ExtractionModel { get; set; }

    /// <summary>
    /// 每个用户的最大实体数（null=不限制）
    /// </summary>
    /// <remarks>超过限制时只返回最近提及的实体。</remarks>
    public int? MaxEntitiesPerUser { get; set; }

    /// <summary>
    /// 实体过期时间（基于 LastMentioned，null=不过期）
    /// </summary>
    public TimeSpan? EntityExpiration { get; set; }
}
