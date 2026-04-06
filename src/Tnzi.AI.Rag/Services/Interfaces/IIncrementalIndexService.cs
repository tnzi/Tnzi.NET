namespace Tnzi.AI.Rag.Services.Interfaces;

/// <summary>
/// 增量索引服务接口 — 文档内容变更时只重新切块变更部分，避免全量 re-chunk
/// </summary>
public interface IIncrementalIndexService
{
    /// <summary>
    /// 对已存在的文档执行增量索引：比较新旧内容，仅对变更段落重新切块和嵌入
    /// </summary>
    /// <param name="documentId">文档 ID</param>
    /// <param name="newContent">新的文本内容</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>增量索引结果</returns>
    Task<IncrementalIndexResult> ReindexAsync(Guid documentId, string newContent, CancellationToken ct = default);
}
