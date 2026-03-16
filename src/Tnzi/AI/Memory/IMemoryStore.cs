namespace Tnzi.AI.Memory;

/// <summary>
/// 记忆范围 — 支持多层级隔离（User/Agent/Session）
/// </summary>
/// <remarks>
/// <para>对齐 Mem0/OpenAI 的分层记忆架构：Name 对应记忆类别，UserId 实现用户隔离，AgentId 实现 Agent 隔离。</para>
/// <para>支持从裸 string 隐式转换，保持向后兼容。</para>
/// </remarks>
[ExperimentalApi(Reason = "AI abstractions are evolving")]
public record MemoryScope(string Name, Guid? UserId = null, Guid? AgentId = null, string? SessionId = null)
{
    /// <summary>
    /// 生成隔离键，用于标识唯一记忆范围
    /// </summary>
    /// <remarks>
    /// 格式: "user:{userId}:agent:{agentId}:{name}" 或在无 userId/agentId 时降级为 "{name}"，
    /// 保持与旧数据兼容。
    /// </remarks>
    public string ToScopeKey()
    {
        var parts = new List<string>(4);

        if (UserId.HasValue)
            parts.Add($"user:{UserId.Value:N}");
        if (AgentId.HasValue)
            parts.Add($"agent:{AgentId.Value:N}");
        if (!string.IsNullOrEmpty(SessionId))
            parts.Add($"session:{SessionId}");

        parts.Add(Name);

        return string.Join(":", parts);
    }

    /// <summary>
    /// 从裸 string 隐式转换（兼容旧 API）
    /// </summary>
    public static implicit operator MemoryScope(string name) => new(name);
}

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

    // --- MemoryScope 重载（default interface methods，兼容现有实现）---

    /// <summary>
    /// 读取指定 scope 的记忆内容（含用户/Agent 隔离）
    /// </summary>
    Task<string?> ReadAsync(MemoryScope scope, CancellationToken ct = default)
        => ReadAsync(scope.ToScopeKey(), ct);

    /// <summary>
    /// 写入或替换指定 scope 的记忆内容（含用户/Agent 隔离）
    /// </summary>
    Task WriteAsync(MemoryScope scope, string content, CancellationToken ct = default)
        => WriteAsync(scope.ToScopeKey(), content, ct);

    /// <summary>
    /// 追加内容到指定 scope 的记忆（含用户/Agent 隔离）
    /// </summary>
    Task AppendAsync(MemoryScope scope, string entry, CancellationToken ct = default)
        => AppendAsync(scope.ToScopeKey(), entry, ct);

    /// <summary>
    /// 搜索指定 scope 的记忆（含用户/Agent 隔离）
    /// </summary>
    Task<IReadOnlyList<MemorySearchResult>> SearchAsync(MemoryScope scope, string query, int maxResults = 10, CancellationToken ct = default)
        => SearchAsync(scope.ToScopeKey(), query, maxResults, ct);

    /// <summary>
    /// 清除指定 scope 的记忆（含用户/Agent 隔离）
    /// </summary>
    Task ClearAsync(MemoryScope scope, CancellationToken ct = default)
        => ClearAsync(scope.ToScopeKey(), ct);

    // --- Entry-level operations (default interface methods, backward compatible) ---

    /// <summary>
    /// 按 ID 更新指定条目的内容
    /// </summary>
    Task UpdateEntryAsync(string scope, Guid entryId, string newContent, CancellationToken ct = default)
        => Task.CompletedTask;

    /// <summary>
    /// 按 ID 删除指定条目
    /// </summary>
    Task DeleteEntryAsync(string scope, Guid entryId, CancellationToken ct = default)
        => Task.CompletedTask;

    /// <summary>
    /// 按类别搜索指定 scope 的记忆
    /// </summary>
    Task<IReadOnlyList<MemorySearchResult>> SearchByCategoryAsync(
        string scope, string query, string category, int maxResults = 10,
        CancellationToken ct = default)
        => SearchAsync(scope, query, maxResults, ct);

    /// <summary>
    /// 追加带元数据的内容到指定 scope 的记忆
    /// </summary>
    Task AppendAsync(string scope, string entry, double importance, string? category, CancellationToken ct = default)
        => AppendAsync(scope, entry, ct);

    /// <summary>
    /// 追加带元数据的内容到指定 scope 的记忆（含用户/Agent 隔离）
    /// </summary>
    Task AppendAsync(MemoryScope scope, string entry, double importance, string? category, CancellationToken ct = default)
        => AppendAsync(scope.ToScopeKey(), entry, importance, category, ct);
}

/// <summary>
/// 记忆搜索结果
/// </summary>
[ExperimentalApi(Reason = "AI abstractions are evolving")]
public class MemorySearchResult
{
    /// <summary>
    /// 条目唯一标识符（用于更新/删除）
    /// </summary>
    public Guid? Id { get; set; }

    /// <summary>
    /// 匹配的内容
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 来源标识
    /// </summary>
    public string? Source { get; set; }

    /// <summary>
    /// 记忆类别（preference, fact, decision, pattern, instruction）
    /// </summary>
    public string? Category { get; set; }

    /// <summary>
    /// 相关度分数（0-1，越高越相关）
    /// </summary>
    public double Score { get; set; }
}
