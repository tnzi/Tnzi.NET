
namespace Tnzi.AI.Memory;

/// <summary>
/// 记忆 scope 解析器：构建本地 scope（可选按用户 / Agent 隔离）与可选的项目快照 scope。
/// </summary>
/// <remarks>
/// 记忆的读取与检索合并的是本地 scope、项目快照 scope 与配置的共享 scope（<c>SharedScope</c>）三处，
/// 没有更多分层。
/// </remarks>
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
