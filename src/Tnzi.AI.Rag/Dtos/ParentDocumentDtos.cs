namespace Tnzi.AI.Rag.Dtos;

/// <summary>
/// Parent Document Retrieval 选项
/// </summary>
public record ParentRetrievalOptions
{
    /// <summary>
    /// 窗口大小 — 匹配块前后各扩展的块数（默认 2）
    /// </summary>
    public int WindowSize { get; init; } = 2;

    /// <summary>
    /// 每个合并结果的最大 Token 数（默认 4000）
    /// </summary>
    public int MaxTokens { get; init; } = 4000;
}

/// <summary>
/// Parent Document Retrieval 结果 — 合并后的扩展上下文块
/// </summary>
public record ParentDocumentResult
{
    /// <summary>所属文档 ID</summary>
    public Guid DocumentId { get; init; }

    /// <summary>合并块的起始索引</summary>
    public int StartChunkIndex { get; init; }

    /// <summary>合并块的结束索引（含）</summary>
    public int EndChunkIndex { get; init; }

    /// <summary>合并后的完整内容</summary>
    public string MergedContent { get; init; } = string.Empty;

    /// <summary>最高匹配分数（多个匹配块合并时取最大值）</summary>
    public double Score { get; init; }

    /// <summary>文档名称</summary>
    public string? DocumentName { get; init; }
}
