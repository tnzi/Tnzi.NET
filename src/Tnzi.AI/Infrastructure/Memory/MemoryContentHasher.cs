namespace Tnzi.AI.Infrastructure.Memory;

/// <summary>
/// MemoryEntry 内容哈希辅助 — 计算 <see cref="Entities.MemoryEntry.ContentHash"/>。
/// 所有写入路径（DatabaseMemoryStore / AgentMemoryService）统一经此计算，
/// 保证 (Scope, ContentHash) 过滤唯一索引的比较基准一致。
/// </summary>
public static class MemoryContentHasher
{
    /// <summary>
    /// 计算内容的 SHA256 哈希（64 位 hex，小写）。空白内容返回 null（不参与唯一约束）。
    /// </summary>
    public static string? Compute(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexStringLower(bytes);
    }
}
