namespace Tnzi.AI.Rag.Dtos;

/// <summary>
/// 增量索引结果 — 记录变更区域 re-chunking 的详细信息
/// </summary>
public record IncrementalIndexResult
{
    /// <summary>
    /// 是否成功
    /// </summary>
    public bool Succeeded { get; init; }

    /// <summary>
    /// 新增的块数
    /// </summary>
    public int AddedChunks { get; init; }

    /// <summary>
    /// 删除的块数
    /// </summary>
    public int RemovedChunks { get; init; }

    /// <summary>
    /// 未变更的块数
    /// </summary>
    public int UnchangedChunks { get; init; }

    /// <summary>
    /// 新的内容哈希（SHA256）
    /// </summary>
    public string NewContentHash { get; init; } = string.Empty;

    /// <summary>
    /// 是否使用了全量重建（>50% 段落变更时回退）
    /// </summary>
    public bool FullReindexUsed { get; init; }

    /// <summary>
    /// 错误信息
    /// </summary>
    public string? ErrorMessage { get; init; }

    public static IncrementalIndexResult Success(int added, int removed, int unchanged, string contentHash, bool fullReindex = false)
        => new()
        {
            Succeeded = true,
            AddedChunks = added,
            RemovedChunks = removed,
            UnchangedChunks = unchanged,
            NewContentHash = contentHash,
            FullReindexUsed = fullReindex
        };

    public static IncrementalIndexResult Failure(string error)
        => new() { Succeeded = false, ErrorMessage = error };
}
