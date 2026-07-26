namespace Tnzi.Finance.Controllers.Admin;

/// <summary>
/// 单据附件与讨论控制器
/// </summary>
/// <remarks>
/// 路由按 <c>{docType}/{docId}</c> 多态寻址，与总账来源令牌同一套词汇——消费应用
/// 自己的单据类型不必改一行代码就能挂附件、开讨论。
///
/// 附件与讨论**各有一套读码**：能看单据不等于该看得到内部讨论（那里常写着不适合
/// 外传的判断）。
/// </remarks>
[Route("admin/finance/documents")]
[DefaultController]
[ApiAuthorize(PermissionName = "finance.document.view")]
public class DefaultFinanceDocumentCollaborationAdminController : ApiAdminControllerBase
{
    private readonly IDocumentAttachmentService _attachments;
    private readonly IDocumentCommentService _comments;

    public DefaultFinanceDocumentCollaborationAdminController(
        IDocumentAttachmentService attachments, IDocumentCommentService comments)
    {
        _attachments = Check.NotNull(attachments);
        _comments = Check.NotNull(comments);
    }

    protected IDocumentAttachmentService Attachments => _attachments;
    protected IDocumentCommentService Comments => _comments;

    /// <summary>
    /// 某张单据的附件
    /// </summary>
    [HttpGet("{docType}/{docId}/attachments")]
    [ApiAuthorize(PermissionName = "finance.attachment.view")]
    public virtual async Task<ApiResult<List<DocumentAttachmentDto>>> GetAttachments(string docType, string docId)
    {
        var result = await _attachments.ListAsync(docType, docId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 登记一个附件（文件已由前端经 Storage 上传）
    /// </summary>
    [HttpPost("{docType}/{docId}/attachments")]
    [ApiAuthorize(PermissionName = "finance.attachment.create")]
    public virtual async Task<ApiResult<DocumentAttachmentDto>> Attach(string docType, string docId, [FromBody] CreateDocumentAttachmentDto request)
    {
        var result = await _attachments.AttachAsync(docType, docId, request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 移除附件
    /// </summary>
    [HttpDelete("attachments/{id:guid}")]
    [ApiAuthorize(PermissionName = "finance.attachment.delete")]
    public virtual async Task<ApiResult> RemoveAttachment(Guid id)
    {
        var result = await _attachments.RemoveAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 批量取附件数（列表页的回形针角标）
    /// </summary>
    [HttpPost("{docType}/attachment-counts")]
    [ApiAuthorize(PermissionName = "finance.attachment.view")]
    public virtual async Task<ApiResult<Dictionary<string, int>>> AttachmentCounts(string docType, [FromBody] List<string> docIds)
    {
        var result = await _attachments.CountBySourceAsync(docType, docIds ?? []);
        return result.ToApiResult();
    }

    /// <summary>
    /// 某张单据的讨论
    /// </summary>
    [HttpGet("{docType}/{docId}/comments")]
    [ApiAuthorize(PermissionName = "finance.comment.view")]
    public virtual async Task<ApiResult<List<DocumentCommentDto>>> GetComments(string docType, string docId)
    {
        var result = await _comments.ListAsync(docType, docId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 发一条讨论
    /// </summary>
    [HttpPost("{docType}/{docId}/comments")]
    [ApiAuthorize(PermissionName = "finance.comment.create")]
    public virtual async Task<ApiResult<DocumentCommentDto>> PostComment(string docType, string docId, [FromBody] CreateDocumentCommentDto request)
    {
        var result = await _comments.PostAsync(docType, docId, request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除一条讨论（作者本人无需额外授权；删他人的需 finance.comment.delete）
    /// </summary>
    [HttpDelete("comments/{id:guid}")]
    public virtual async Task<ApiResult> DeleteComment(Guid id)
    {
        var result = await _comments.DeleteAsync(id);
        return result.ToApiResult();
    }
}
