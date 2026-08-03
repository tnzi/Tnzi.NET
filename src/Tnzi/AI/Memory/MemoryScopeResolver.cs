
namespace Tnzi.AI.Memory;

/// <summary>
/// 记忆 scope 解析器 - 统一构建用户/Agent/项目三级记忆范围。
/// </summary>
[ExperimentalApi(Reason = "AI abstractions are evolving")]
public static class MemoryScopeResolver
{
    public static MemoryScope BuildLocalScope(
        string scopeName,
        bool enableUserIsolation,
        bool enableAgentIsolation,
        Guid? userId = null,
        Guid? agentId = null)
    {
        var resolvedScopeName = string.IsNullOrWhiteSpace(scopeName) ? "default" : scopeName.Trim();
        return new MemoryScope(
            resolvedScopeName,
            enableUserIsolation ? userId : null,
            enableAgentIsolation ? agentId : null);
    }

    public static string? ResolveProjectSnapshotScope(
        bool enableProjectSnapshot,
        string? projectSnapshotScopePrefix,
        IReadOnlyDictionary<string, object>? metadata)
    {
        if (!enableProjectSnapshot || metadata == null || metadata.Count == 0)
        {
            return null;
        }

        var projectRoot = GetMetadataString(metadata, "project_root")
            ?? GetMetadataString(metadata, "projectRoot")
            ?? GetMetadataString(metadata, "working_directory")
            ?? GetMetadataString(metadata, "workingDirectory");

        return ResolveProjectSnapshotScope(enableProjectSnapshot, projectSnapshotScopePrefix, projectRoot);
    }

    public static string? ResolveProjectSnapshotScope(
        bool enableProjectSnapshot,
        string? projectSnapshotScopePrefix,
        string? projectRoot)
    {
        if (!enableProjectSnapshot || string.IsNullOrWhiteSpace(projectRoot))
        {
            return null;
        }

        string normalizedRoot;
        try
        {
            normalizedRoot = Path.GetFullPath(projectRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(normalizedRoot))
        {
            return null;
        }

        var prefix = string.IsNullOrWhiteSpace(projectSnapshotScopePrefix)
            ? "project"
            : projectSnapshotScopePrefix.Trim();
        var slug = CreateDirectorySlug(normalizedRoot);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            normalizedRoot.ToLowerInvariant())))[..12].ToLowerInvariant();

        return $"{prefix}:{slug}:{hash}";
    }

    private static string CreateDirectorySlug(string normalizedRoot)
    {
        var directoryName = Path.GetFileName(normalizedRoot);
        if (string.IsNullOrWhiteSpace(directoryName))
        {
            directoryName = normalizedRoot.Replace(':', '_');
        }

        var builder = new StringBuilder(directoryName.Length);
        foreach (var ch in directoryName.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
                continue;
            }

            if (builder.Length == 0 || builder[^1] == '-')
            {
                continue;
            }

            builder.Append('-');
        }

        var slug = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? "project" : slug;
    }

    /// <summary>
    /// 构建 Agent Type 级别的记忆 scope - 同一 type（如 "researcher", "coder"）
    /// 的不同 agent 实例共享 type scope 记忆。
    /// </summary>
    /// <param name="agentType">Agent 类型名称（来自 SubAgentTypeDefinition.Name）</param>
    /// <param name="userId">用户 ID（可选，启用用户隔离时传入）</param>
    /// <returns>Agent type 级别的 MemoryScope</returns>
    public static MemoryScope BuildAgentTypeScope(string agentType, Guid? userId = null)
    {
        var resolvedType = string.IsNullOrWhiteSpace(agentType) ? "default" : agentType.Trim().ToLowerInvariant();
        return new MemoryScope($"agent-type:{resolvedType}", userId);
    }

    /// <summary>
    /// 构建带 local override 层的记忆 scope - 用户本地覆盖层，
    /// 读取优先级: local override > project snapshot > system。
    /// </summary>
    /// <param name="userId">用户 ID</param>
    /// <param name="agentId">Agent ID（可选）</param>
    /// <returns>本地覆盖层 MemoryScope</returns>
    public static MemoryScope BuildLocalOverrideScope(Guid userId, Guid? agentId = null)
    {
        return new MemoryScope($"local:{userId:N}", userId, agentId);
    }

    /// <summary>
    /// 按优先级排列所有应检索的记忆 scope 列表（local override > project snapshot > system）。
    /// 调用者应依次读取并合并结果，前者的记忆优先于后者。
    /// </summary>
    /// <param name="userId">用户 ID</param>
    /// <param name="agentId">Agent ID（可选）</param>
    /// <param name="projectSnapshotScope">项目快照 scope name（可选，由 ResolveProjectSnapshotScope 生成）</param>
    /// <param name="systemScopeName">系统级 scope name</param>
    /// <returns>按优先级降序排列的 scope 列表</returns>
    public static IReadOnlyList<MemoryScope> BuildLayeredScopes(
        Guid userId,
        Guid? agentId = null,
        string? projectSnapshotScope = null,
        string systemScopeName = "default")
    {
        var scopes = new List<MemoryScope>(3)
        {
            // Highest priority: local override
            BuildLocalOverrideScope(userId, agentId)
        };

        // Medium priority: project snapshot
        if (!string.IsNullOrWhiteSpace(projectSnapshotScope))
        {
            scopes.Add(new MemoryScope(projectSnapshotScope, userId, agentId));
        }

        // Lowest priority: system
        scopes.Add(new MemoryScope(systemScopeName, userId, agentId));

        return scopes;
    }

    /// <summary>
    /// Resolves synchronization metadata for a project snapshot scope.
    /// </summary>
    /// <param name="scopeName">The scope name (returned by <see cref="ResolveProjectSnapshotScope(bool, string?, string?)"/>)</param>
    /// <param name="projectRoot">The project root directory path</param>
    /// <returns>Snapshot sync metadata, or null if the scope name or project root is invalid</returns>
    public static SnapshotSyncMetadata? GetSnapshotMetadata(string? scopeName, string? projectRoot)
    {
        if (string.IsNullOrWhiteSpace(scopeName) || string.IsNullOrWhiteSpace(projectRoot))
        {
            return null;
        }

        string normalizedRoot;
        try
        {
            normalizedRoot = Path.GetFullPath(projectRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(normalizedRoot))
        {
            return null;
        }

        var contentHash = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(normalizedRoot.ToLowerInvariant()))).ToLowerInvariant();

        // 检查 scope 名称中的 hash 部分与计算结果是否一致（标识是否为同一路径生成的 scope）
        var isOverridden = !scopeName.EndsWith(contentHash[..12], StringComparison.Ordinal);

        return new SnapshotSyncMetadata(scopeName, normalizedRoot, contentHash, DateTime.UtcNow, isOverridden);
    }

    /// <summary>
    /// Asynchronously resolves snapshot sync metadata - delegates to <see cref="GetSnapshotMetadata"/>.
    /// </summary>
    public static Task<SnapshotSyncMetadata?> GetSnapshotMetadataAsync(
        string? scopeName, string? projectRoot, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(GetSnapshotMetadata(scopeName, projectRoot));
    }

    private static string? GetMetadataString(IReadOnlyDictionary<string, object> metadata, string key)
    {
        if (!metadata.TryGetValue(key, out var value) || value == null)
        {
            return null;
        }

        var text = value as string ?? Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }
}

/// <summary>
/// 项目记忆快照的同步元数据。
/// </summary>
/// <param name="ScopeName">快照 scope 名称（如 "project:tnzi-net:abc123def456"）</param>
/// <param name="SourcePath">项目根目录的规范化绝对路径</param>
/// <param name="ContentHash">基于项目路径的 SHA-256 完整哈希（小写十六进制）</param>
/// <param name="CreatedAt">元数据解析时间 (UTC)</param>
/// <param name="IsOverridden">scope 名称是否已被覆盖（hash 不匹配原始路径）</param>
[ExperimentalApi(Reason = "AI abstractions are evolving")]
public record SnapshotSyncMetadata(
    string ScopeName,
    string SourcePath,
    string ContentHash,
    DateTime CreatedAt,
    bool IsOverridden);
