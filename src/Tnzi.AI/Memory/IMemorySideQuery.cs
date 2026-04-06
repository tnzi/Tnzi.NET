namespace Tnzi.AI.Memory;

/// <summary>
/// 记忆侧查询 — 用轻量 LLM 调用预筛记忆相关性。
/// 与 SearchAsync 的向量匹配互补：SearchAsync 用向量，SideQuery 用语义判断。
/// </summary>
public interface IMemorySideQuery
{
    /// <summary>
    /// 评估记忆条目与当前查询的相关性
    /// </summary>
    /// <param name="entries">记忆条目（category + snippet）</param>
    /// <param name="query">用户查询</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>相关条目的索引列表（按相关性排序）</returns>
    Task<IReadOnlyList<int>> FilterRelevantAsync(
        IReadOnlyList<MemorySnippet> entries, string query, CancellationToken ct = default);
}

/// <summary>
/// 记忆片段（用于侧查询的轻量数据）
/// </summary>
public record MemorySnippet(int Index, string? Category, string ContentPreview);
