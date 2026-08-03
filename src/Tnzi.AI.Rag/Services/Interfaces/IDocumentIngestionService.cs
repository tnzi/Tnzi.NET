namespace Tnzi.AI.Rag.Services;

/// <summary>
/// 文档摄取服务接口 - 负责提取文本、切块、嵌入、存储到向量数据库
/// </summary>
public interface IDocumentIngestionService
{
    /// <summary>
    /// 摄取文档到知识库
    /// </summary>
    /// <param name="kbId">知识库 ID</param>
    /// <param name="documentId">文档 ID（与 KnowledgeDocument.Id 关联）</param>
    /// <param name="content">文件流</param>
    /// <param name="fileName">文件名</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>摄取结果</returns>
    Task<IngestResult> IngestAsync(Guid kbId, Guid documentId, Stream content, string fileName, CancellationToken ct = default);
}
