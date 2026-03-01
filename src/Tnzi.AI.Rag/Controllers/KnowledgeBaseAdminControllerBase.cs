namespace Tnzi.AI.Rag.Controllers;

/// <summary>
/// 知识库管理控制器基类
/// </summary>
[Route("admin/knowledge-bases")]
[ApiExplorerSettings(GroupName = "rag-admin")]
public abstract class KnowledgeBaseAdminControllerBase : ApiAdminControllerBase
{
    protected readonly IKnowledgeBaseService KnowledgeBaseService;

    protected KnowledgeBaseAdminControllerBase(IKnowledgeBaseService knowledgeBaseService)
    {
        KnowledgeBaseService = Check.NotNull(knowledgeBaseService);
    }

    /// <summary>
    /// 创建知识库
    /// </summary>
    [HttpPost]
    public virtual async Task<ApiResult<KnowledgeBaseDto>> Create([FromBody] CreateKnowledgeBaseDto input, CancellationToken ct)
    {
        var result = await KnowledgeBaseService.CreateAsync(input, ct);
        return result.ToApiResult();
    }

    /// <summary>
    /// 更新知识库
    /// </summary>
    [HttpPut("{id:guid}")]
    public virtual async Task<ApiResult<KnowledgeBaseDto>> Update(Guid id, [FromBody] UpdateKnowledgeBaseDto input, CancellationToken ct)
    {
        var result = await KnowledgeBaseService.UpdateAsync(id, input, ct);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除知识库
    /// </summary>
    [HttpDelete("{id:guid}")]
    public virtual async Task<ApiResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await KnowledgeBaseService.DeleteAsync(id, ct);
        return result.ToApiResult();
    }

    /// <summary>
    /// 根据 ID 获取知识库
    /// </summary>
    [HttpGet("{id:guid}")]
    public virtual async Task<ApiResult<KnowledgeBaseDto>> GetById(Guid id, CancellationToken ct)
    {
        var result = await KnowledgeBaseService.GetByIdAsync(id, ct);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取知识库列表
    /// </summary>
    [HttpPost("query")]
    public virtual async Task<ApiResult<IPagedList<KnowledgeBaseDto>>> GetList([FromBody] KnowledgeBaseQueryDto query, CancellationToken ct)
    {
        var result = await KnowledgeBaseService.GetListAsync(query, ct);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取知识库的文档分页列表
    /// </summary>
    [HttpPost("{id:guid}/documents/query")]
    public virtual async Task<ApiResult<IPagedList<KnowledgeDocumentDto>>> GetDocuments(Guid id, [FromBody] DocumentQueryDto query, CancellationToken ct)
    {
        var result = await KnowledgeBaseService.GetDocumentsAsync(id, query, ct);
        return result.ToApiResult();
    }

    /// <summary>
    /// 上传文档到知识库
    /// </summary>
    [HttpPost("{id:guid}/upload")]
    public virtual async Task<ApiResult<DocumentUploadResultDto>> UploadDocument(Guid id, IFormFile file, CancellationToken ct)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest<DocumentUploadResultDto>("File is required");
        }

        using var stream = file.OpenReadStream();
        var result = await KnowledgeBaseService.UploadDocumentAsync(
            id, stream, file.FileName, file.ContentType, file.Length, ct);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除知识库中的文档
    /// </summary>
    [HttpDelete("{id:guid}/documents/{docId:guid}")]
    public virtual async Task<ApiResult> DeleteDocument(Guid id, Guid docId, CancellationToken ct)
    {
        var result = await KnowledgeBaseService.DeleteDocumentAsync(id, docId, ct);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取文档处理状态（用于异步摄取的轮询查询）
    /// </summary>
    [HttpGet("{id:guid}/documents/{docId:guid}/status")]
    public virtual async Task<ApiResult<KnowledgeDocumentDto>> GetDocumentStatus(Guid id, Guid docId, CancellationToken ct)
    {
        var result = await KnowledgeBaseService.GetDocumentStatusAsync(id, docId, ct);
        return result.ToApiResult();
    }

    /// <summary>
    /// 在指定知识库中搜索（支持 metadata 过滤）
    /// </summary>
    [HttpPost("{id:guid}/search")]
    public virtual async Task<ApiResult<List<SearchResultDto>>> Search(Guid id, [FromBody] SearchRequestDto request, CancellationToken ct)
    {
        var result = await KnowledgeBaseService.SearchAsync(id, request.Query, request.TopK, request.MetadataFilter, ct);
        return result.ToApiResult();
    }

    /// <summary>
    /// 跨所有知识库搜索（支持 metadata 过滤）
    /// </summary>
    [HttpPost("search-all")]
    public virtual async Task<ApiResult<List<SearchResultDto>>> SearchAll([FromBody] SearchRequestDto request, CancellationToken ct)
    {
        var result = await KnowledgeBaseService.SearchAllAsync(request.Query, request.TopK, request.MetadataFilter, ct);
        return result.ToApiResult();
    }
}
