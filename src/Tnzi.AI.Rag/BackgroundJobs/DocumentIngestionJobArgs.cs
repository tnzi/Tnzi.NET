namespace Tnzi.AI.Rag.BackgroundJobs;

/// <summary>
/// 文档摄取后台任务参数
/// </summary>
public class DocumentIngestionJobArgs : ITenantAwareJobArgs
{
    /// <inheritdoc />
    public Guid? TenantId { get; set; }

    /// <summary>
    /// 知识库 ID
    /// </summary>
    public Guid KnowledgeBaseId { get; set; }

    /// <summary>
    /// 文档 ID
    /// </summary>
    public Guid DocumentId { get; set; }

    /// <summary>
    /// 文件内容（序列化后的字节数组）
    /// </summary>
    public byte[] FileContent { get; set; } = [];

    /// <summary>
    /// 文件名
    /// </summary>
    public string FileName { get; set; } = string.Empty;
}
