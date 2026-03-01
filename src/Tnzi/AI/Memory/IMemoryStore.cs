namespace Tnzi.AI.Memory;

/// <summary>
/// 记忆存储抽象 — 持久化 Agent 记忆
/// </summary>
/// <remarks>
/// 默认实现为 DatabaseMemoryStore（使用 EF Core，由 Tnzi.AI 模块提供）。
/// Tnzi.AI.Coder 模块提供 FileMemoryStore 替代实现（文件系统存储）。
/// </remarks>
[ExperimentalApi(Reason = "AI abstractions are evolving")]
public interface IMemoryStore
{
    /// <summary>
    /// 读取指定 scope 的记忆内容
    /// </summary>
    /// <param name="scope">记忆范围（如 "default", "project-x"）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>记忆内容，不存在时返回 null</returns>
    Task<string?> ReadAsync(string scope, CancellationToken ct = default);

    /// <summary>
    /// 写入或替换指定 scope 的记忆内容
    /// </summary>
    /// <param name="scope">记忆范围</param>
    /// <param name="content">记忆内容</param>
    /// <param name="ct">取消令牌</param>
    Task WriteAsync(string scope, string content, CancellationToken ct = default);

    /// <summary>
    /// 追加内容到指定 scope 的记忆
    /// </summary>
    /// <param name="scope">记忆范围</param>
    /// <param name="entry">要追加的内容</param>
    /// <param name="ct">取消令牌</param>
    Task AppendAsync(string scope, string entry, CancellationToken ct = default);

    /// <summary>
    /// 搜索指定 scope 的记忆
    /// </summary>
    /// <param name="scope">记忆范围</param>
    /// <param name="query">搜索关键词</param>
    /// <param name="maxResults">最大结果数</param>
    /// <param name="ct">取消令牌</param>
    Task<IReadOnlyList<MemorySearchResult>> SearchAsync(string scope, string query, int maxResults = 10, CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<MemorySearchResult>>([]);
    }

    /// <summary>
    /// 清除指定 scope 的记忆
    /// </summary>
    /// <param name="scope">记忆范围</param>
    /// <param name="ct">取消令牌</param>
    Task ClearAsync(string scope, CancellationToken ct = default);
}

/// <summary>
/// 记忆搜索结果
/// </summary>
[ExperimentalApi(Reason = "AI abstractions are evolving")]
public class MemorySearchResult
{
    /// <summary>
    /// 匹配的内容
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 来源标识
    /// </summary>
    public string? Source { get; set; }

    /// <summary>
    /// 相关度分数（0-1，越高越相关）
    /// </summary>
    public double Score { get; set; }
}
