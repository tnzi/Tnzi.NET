namespace Tnzi.AI.Memory;

/// <summary>
/// 记忆范围 - 支持多层级隔离（User/Agent/Session）
/// </summary>
/// <remarks>
/// <para>对齐 Mem0/OpenAI 的分层记忆架构：Name 对应记忆类别，UserId 实现用户隔离，AgentId 实现 Agent 隔离。</para>
/// <para>支持从裸 string 隐式转换，保持向后兼容。</para>
/// </remarks>
[ExperimentalApi(Reason = "AI abstractions are evolving")]
public record MemoryScope(string Name, Guid? UserId = null, Guid? AgentId = null, string? SessionId = null)
{
    /// <summary>
    /// Agent-bound 标志 - 标识这是绑定到某个 Agent 的记忆范围。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 为 <c>true</c> 时，该范围的记忆通过结构化的 <c>AgentId</c> 列检索（而非依赖当前用户的 scope 字符串），
    /// 因此即便在 headless 运行（无 <c>ICurrentUser</c>）下，写入与读取也能确定性地匹配。
    /// </para>
    /// <para>
    /// 适用场景：管理员为某个 Agent 预置的人设/知识记忆，无论谁触发该 Agent 的运行都应被加载。
    /// 默认 <c>false</c>，保持现有 scope 键和数据语义不变。
    /// </para>
    /// </remarks>
    public bool AgentBound { get; init; }

    /// <summary>
    /// 生成隔离键，用于标识唯一记忆范围
    /// </summary>
    /// <remarks>
    /// 格式: "user:{userId}:agent:{agentId}:{name}" 或在无 userId/agentId 时降级为 "{name}"，
    /// 保持与旧数据兼容。Agent-bound 范围生成与当前用户无关的确定性键
    /// "agent-bound:{agentId}:{name}"，确保 headless 读写一致。
    /// </remarks>
    public string ToScopeKey()
    {
        // Agent-bound: 与当前用户无关的确定性键，确保 headless 写入/读取匹配。
        if (AgentBound && AgentId.HasValue)
        {
            return $"agent-bound:{AgentId.Value:N}:{Name}";
        }

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
    /// 构建一个 Agent-bound 记忆范围 - 记忆绑定到指定 Agent，
    /// 通过结构化 <c>AgentId</c> 列检索，与当前用户无关（headless-safe）。
    /// </summary>
    /// <param name="agentId">目标 Agent ID</param>
    /// <param name="name">记忆类别名（默认 "default"）</param>
    public static MemoryScope ForAgent(Guid agentId, string name = "default")
        => new(string.IsNullOrWhiteSpace(name) ? "default" : name.Trim(), AgentId: agentId) { AgentBound = true };

    /// <summary>
    /// 从裸 string 隐式转换（兼容旧 API）
    /// </summary>
    public static implicit operator MemoryScope(string name) => new(name);
}

/// <summary>
/// 记忆存储抽象 - 持久化 Agent 记忆
/// </summary>
/// <remarks>
/// 默认实现为 DatabaseMemoryStore（使用 EF Core，由 Tnzi.AI 模块提供）。
/// 应用可注册自定义 IMemoryStore 实现替换（如文件系统存储）。
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
    //
    // Agent-bound 范围（scope.AgentBound == true）走结构化 AgentId 列检索路径
    // （ReadByAgentAsync / SearchByAgentAsync），从而修复"写入 AgentId 列但读取仅按
    // Scope 字符串过滤、导致 Agent 记忆永不命中"的缺陷，并支持 headless 运行。
    // 默认实现回退到字符串 scope 路径，使现有 IMemoryStore 实现（如 FileMemoryStore）
    // 无需改动即可继续工作（向后兼容）。

    /// <summary>
    /// 读取指定 scope 的记忆内容（含用户/Agent 隔离）
    /// </summary>
    Task<string?> ReadAsync(MemoryScope scope, CancellationToken ct = default)
        => scope is { AgentBound: true, AgentId: { } agentId }
            ? ReadByAgentAsync(agentId, scope.Name, ct)
            : ReadAsync(scope.ToScopeKey(), ct);

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
        => scope is { AgentBound: true, AgentId: { } agentId }
            ? SearchByAgentAsync(agentId, scope.Name, query, maxResults, ct)
            : SearchAsync(scope.ToScopeKey(), query, maxResults, ct);

    /// <summary>
    /// 清除指定 scope 的记忆（含用户/Agent 隔离）
    /// </summary>
    Task ClearAsync(MemoryScope scope, CancellationToken ct = default)
        => ClearAsync(scope.ToScopeKey(), ct);

    // --- Agent-bound 结构化检索（default interface methods，向后兼容）---

    /// <summary>
    /// 按结构化 <c>AgentId</c> 列读取绑定到该 Agent 的记忆内容。
    /// </summary>
    /// <remarks>
    /// 默认实现回退到字符串 scope 路径（<see cref="MemoryScope.ToScopeKey()"/> 生成的
    /// "agent-bound:{agentId}:{name}" 键），使不支持结构化列的实现仍能工作。
    /// <c>DatabaseMemoryStore</c> 重写为按 <c>AgentId</c> 列过滤。
    /// </remarks>
    Task<string?> ReadByAgentAsync(Guid agentId, string scope, CancellationToken ct = default)
        => ReadAsync(MemoryScope.ForAgent(agentId, scope).ToScopeKey(), ct);

    /// <summary>
    /// 按结构化 <c>AgentId</c> 列搜索绑定到该 Agent 的记忆。
    /// </summary>
    /// <remarks>
    /// 默认实现回退到字符串 scope 路径，使不支持结构化列的实现仍能工作。
    /// <c>DatabaseMemoryStore</c> 重写为按 <c>AgentId</c> 列过滤。
    /// </remarks>
    Task<IReadOnlyList<MemorySearchResult>> SearchByAgentAsync(
        Guid agentId, string scope, string query, int maxResults = 10, CancellationToken ct = default)
        => SearchAsync(MemoryScope.ForAgent(agentId, scope).ToScopeKey(), query, maxResults, ct);

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
