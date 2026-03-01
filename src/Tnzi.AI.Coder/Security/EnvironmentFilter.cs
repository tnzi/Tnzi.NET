namespace Tnzi.AI.Coder.Security;

/// <summary>
/// 环境变量过滤器 — 根据沙箱配置过滤子进程的环境变量
/// </summary>
public static class EnvironmentFilter
{
    /// <summary>
    /// 对 ProcessStartInfo 应用环境变量过滤
    /// </summary>
    /// <remarks>
    /// 白名单优先：若白名单非空，仅传递白名单中的变量；
    /// 否则传递所有变量但排除黑名单中匹配的项（大小写不敏感后缀匹配）。
    /// </remarks>
    public static void ApplyEnvironmentFilter(ProcessStartInfo psi, SandboxOptions sandbox)
    {
        Check.NotNull(psi);
        Check.NotNull(sandbox);

        // 获取当前进程所有环境变量
        var currentEnv = Environment.GetEnvironmentVariables();

        if (sandbox.EnvironmentWhitelist.Count > 0)
        {
            // 白名单模式：仅传递白名单中的变量
            var whitelist = new HashSet<string>(sandbox.EnvironmentWhitelist, StringComparer.OrdinalIgnoreCase);
            psi.Environment.Clear();

            foreach (DictionaryEntry entry in currentEnv)
            {
                var key = entry.Key?.ToString();
                if (key != null && whitelist.Contains(key))
                {
                    psi.Environment[key] = entry.Value?.ToString();
                }
            }
        }
        else if (sandbox.EnvironmentBlacklist.Count > 0)
        {
            // 黑名单模式：排除匹配项（后缀匹配，大小写不敏感）
            var blacklist = sandbox.EnvironmentBlacklist;

            // 收集需要移除的 key
            var keysToRemove = new List<string>();
            foreach (DictionaryEntry entry in currentEnv)
            {
                var key = entry.Key?.ToString();
                if (key != null && IsBlacklisted(key, blacklist))
                {
                    keysToRemove.Add(key);
                }
            }

            foreach (var key in keysToRemove)
            {
                psi.Environment.Remove(key);
            }
        }
    }

    /// <summary>
    /// 检查环境变量名是否被黑名单匹配（大小写不敏感，后缀匹配）
    /// </summary>
    private static bool IsBlacklisted(string key, List<string> blacklist)
    {
        foreach (var pattern in blacklist)
        {
            if (key.EndsWith(pattern, StringComparison.OrdinalIgnoreCase) ||
                key.Equals(pattern, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
