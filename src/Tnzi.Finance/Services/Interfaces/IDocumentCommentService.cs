namespace Tnzi.Finance.Services;

/// <summary>
/// 单据内部讨论
/// </summary>
public interface IDocumentCommentService
{
    /// <summary>列出某张单据的讨论（按时间正序，读起来就是一条线）</summary>
    Task<Result<List<DocumentCommentDto>>> ListAsync(string sourceType, string sourceId, CancellationToken cancellationToken = default);

    /// <summary>发一条</summary>
    Task<Result<DocumentCommentDto>> PostAsync(string sourceType, string sourceId, CreateDocumentCommentDto input, CancellationToken cancellationToken = default);

    /// <summary>删除一条（作者本人；持 finance.comment.delete 者可删任意一条）</summary>
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
