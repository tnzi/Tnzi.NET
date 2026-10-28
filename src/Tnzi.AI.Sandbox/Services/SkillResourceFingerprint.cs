namespace Tnzi.AI.Sandbox.Services;

/// <summary>
/// 技能资源集合的指纹：全部带资源的技能按 slug、资源按相对路径排序后，对 slug / 路径 / 内容求 SHA-256。
/// 线程里的技能副本记着复制时的指纹，与仓库现状比一比就知道要不要重新复制。
/// </summary>
/// <remarks>
/// 技能仓库（文件技能存储）在缓存有效期内每次返回同一个列表实例，指纹按实例缓存：每条线程第一次用到沙箱时
/// 只是一次查表，仓库重新加载之后（新的实例）才重算一次。
/// </remarks>
internal static class SkillResourceFingerprint
{
    private static readonly ConditionalWeakTable<IReadOnlyList<SkillDefinition>, string> Cache = new();

    public static string Of(IReadOnlyList<SkillDefinition> skills)
        => Cache.GetValue(Check.NotNull(skills), Compute);

    private static string Compute(IReadOnlyList<SkillDefinition> skills)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var skill in skills.Where(s => s.Resources.Count > 0).OrderBy(s => s.Slug, StringComparer.Ordinal))
        {
            Append(hash, skill.Slug);
            foreach (var (path, content) in skill.Resources.OrderBy(r => r.Key, StringComparer.Ordinal))
            {
                Append(hash, path);
                Append(hash, content);
            }
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    /// <summary>每一段后跟一个 NUL，相邻两段换个切法不会得到同一串字节。</summary>
    private static void Append(IncrementalHash hash, string value)
    {
        hash.AppendData(Encoding.UTF8.GetBytes(value));
        hash.AppendData([0]);
    }
}
