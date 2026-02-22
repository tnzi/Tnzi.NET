namespace Tnzi.AI.Options;

/// <summary>
/// 嵌入选项
/// </summary>
public class EmbeddingOptions
{
    /// <summary>
    /// 嵌入模型提供商
    /// </summary>
    public string? Provider { get; set; }

    /// <summary>
    /// 嵌入模型名称
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// 输出维度（部分模型支持）
    /// </summary>
    /// <remarks>
    /// 例如 OpenAI 的 text-embedding-3-small 支持 256-1536 维
    /// </remarks>
    public int? Dimensions { get; set; }

    /// <summary>
    /// 其他模型参数
    /// </summary>
    public Dictionary<string, object>? Parameters { get; set; }
}
