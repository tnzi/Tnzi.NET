namespace Tnzi.Finance.Services;

/// <summary>
/// 单据附件（**只记链接不碰 Storage**：文件由前端上传后把 fileId 交进来）
/// </summary>
/// <remarks>
/// 单据以 <c>SourceType</c>+<c>SourceId</c> 多态标识，与总账来源令牌同一套词汇，
/// 因此消费应用自己的单据类型天然也能挂附件——这正是不校验封闭枚举的原因。
/// </remarks>
public interface IDocumentAttachmentService
{
    /// <summary>列出某张单据的全部附件（按时间正序）</summary>
    Task<Result<List<DocumentAttachmentDto>>> ListAsync(string sourceType, string sourceId, CancellationToken cancellationToken = default);

    /// <summary>登记一个附件</summary>
    Task<Result<DocumentAttachmentDto>> AttachAsync(string sourceType, string sourceId, CreateDocumentAttachmentDto input, CancellationToken cancellationToken = default);

    /// <summary>移除一个附件（软删；文件本身由 Storage 的引用跟踪决定去留）</summary>
    Task<Result> RemoveAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>批量取多张单据的附件数（列表页上的回形针角标，避免逐行往返）</summary>
    Task<Result<Dictionary<string, int>>> CountBySourceAsync(string sourceType, IReadOnlyCollection<string> sourceIds, CancellationToken cancellationToken = default);
}
