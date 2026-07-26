namespace Tnzi.AI.Rag.Services.Interfaces;

/// <summary>
/// 知识库服务接口
/// </summary>
public interface IKnowledgeBaseService
{
    /// <summary>
    /// 创建知识库
    /// </summary>
    Task<Result<KnowledgeBaseDto>> CreateAsync(CreateKnowledgeBaseDto input, CancellationToken ct = default);

    /// <summary>
    /// 更新知识库
    /// </summary>
    Task<Result<KnowledgeBaseDto>> UpdateAsync(Guid id, UpdateKnowledgeBaseDto input, CancellationToken ct = default);

    /// <summary>
    /// 删除知识库（级联删除文档和块）
    /// </summary>
    Task<Result> DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// 根据 ID 获取知识库
    /// </summary>
    Task<Result<KnowledgeBaseDto>> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// 获取知识库分页列表
    /// </summary>
    Task<Result<IPagedList<KnowledgeBaseDto>>> GetListAsync(KnowledgeBaseQueryDto query, CancellationToken ct = default);

    /// <summary>
    /// 获取知识库的文档分页列表
    /// </summary>
    Task<Result<IPagedList<KnowledgeDocumentDto>>> GetDocumentsAsync(Guid kbId, DocumentQueryDto query, CancellationToken ct = default);

    /// <summary>
    /// 上传文档到知识库（支持内容去重检测）
    /// </summary>
    Task<Result<DocumentUploadResultDto>> UploadDocumentAsync(Guid kbId, Stream content, string fileName, string? contentType = null, long fileSize = 0, CancellationToken ct = default);

    /// <summary>
    /// 删除知识库中的文档
    /// </summary>
    Task<Result> DeleteDocumentAsync(Guid kbId, Guid documentId, CancellationToken ct = default);

    /// <summary>
    /// 在指定知识库中搜索（支持 metadata 过滤）
    /// </summary>
    Task<Result<List<SearchResultDto>>> SearchAsync(Guid kbId, string query, int topK = 5, Dictionary<string, string>? metadataFilter = null, CancellationToken ct = default);

    /// <summary>
    /// 跨所有启用的知识库搜索（支持 metadata 过滤）
    /// </summary>
    Task<Result<List<SearchResultDto>>> SearchAllAsync(string query, int topK = 5, Dictionary<string, string>? metadataFilter = null, CancellationToken ct = default);

    /// <summary>
    /// 获取文档处理状态（用于异步摄取的轮询查询）
    /// </summary>
    Task<Result<KnowledgeDocumentDto>> GetDocumentStatusAsync(Guid kbId, Guid documentId, CancellationToken ct = default);

    /// <summary>
    /// 对知识库的所有块执行完整重新向量化（admin 触发）
    /// </summary>
    /// <remarks>
    /// 同步前台批量执行：分批读取所有块、调用 IEmbeddingService 批量嵌入、回写 Embedding 字段。
    /// 设计为幂等操作 - 多次执行结果一致。
    /// </remarks>
    Task<Result<ReindexResultDto>> ReindexAsync(Guid knowledgeBaseId, CancellationToken cancellationToken = default);
}
