namespace Tnzi.AI.Mcp.Server;

/// <summary>
/// MCP 工具名的规则：从 Agent 名派生、校验管理员与消费方给的名字、同名时怎么区分。
/// </summary>
/// <remarks>
/// <para>
/// 两档规则。<b>Agent 工具</b>按常见客户端的最严口径 <c>^[A-Za-z0-9_-]{1,64}$</c>（多数 LLM 客户端对工具名就是这个限制，
/// 一个不合规的名字会让客户端拒收整张工具列表）。<b>消费方自定义工具</b>按 SDK 自己的口径
/// <c>^[A-Za-z0-9_.-]{1,128}$</c>：那是名字能被 SDK 接受的底线，比它更严会让此前能用的名字在注册时被拒。
/// </para>
/// <para>
/// ★ 此前的清洗用 <c>char.IsLetterOrDigit</c>：中文、全角等任意 Unicode 字母原样放行、也不限长度。
/// 名为「客服助手」的 Agent 于是得到同名工具，SDK 在建工具时按自己的规则拒绝并抛异常，构建器吞掉异常只记一条 Warning：
/// 管理端暴露答 200、已暴露列表里有它，<c>tools/list</c> 却永远没有它。
/// </para>
/// </remarks>
internal static class McpToolNames
{
    /// <summary>Agent 工具名的最大长度（常见客户端的上限）。</summary>
    public const int MaxAgentToolNameLength = 64;

    /// <summary>同名时追加的区分后缀里取 Agent Id 的位数。</summary>
    private const int DisambiguatorLength = 8;

    private const int MaxCustomToolNameLength = 128;

    /// <summary>从 Agent 名派生工具名：只留 ASCII 字母数字、下划线与连字符，空格变下划线，截到 64 个字符。</summary>
    /// <remarks>一个字符都留不下（全是中文之类）时退回 <c>agent_{Id 前 8 位}</c>，描述里仍是原名。</remarks>
    public static string FromAgentName(string? agentName, Guid agentId)
    {
        var sanitized = new StringBuilder();
        foreach (var c in agentName ?? string.Empty)
        {
            if (char.IsAsciiLetterOrDigit(c) || c is '_' or '-')
            {
                sanitized.Append(c);
            }
            else if (c == ' ')
            {
                sanitized.Append('_');
            }
        }

        var name = sanitized.ToString().Trim('_', '-');
        if (name.Length == 0)
        {
            return $"agent_{Disambiguator(agentId)}";
        }

        return name.Length > MaxAgentToolNameLength ? name[..MaxAgentToolNameLength] : name;
    }

    /// <summary>管理员或调用方给 Agent 工具指定的名字是否合规（<c>^[A-Za-z0-9_-]{1,64}$</c>）。</summary>
    public static bool IsValidAgentToolName(string? name)
        => !string.IsNullOrEmpty(name)
           && name.Length <= MaxAgentToolNameLength
           && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');

    /// <summary>消费方自定义工具名是否合规（SDK 的口径 <c>^[A-Za-z0-9_.-]{1,128}$</c>）。</summary>
    public static bool IsValidCustomToolName(string? name)
        => !string.IsNullOrEmpty(name)
           && name.Length <= MaxCustomToolNameLength
           && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.');

    /// <summary>
    /// 同名时的区分形式：<c>{名字}_{Id 前 8 位}</c>，总长仍不超过 64。
    /// </summary>
    /// <remarks>
    /// 撞名的每一方都改用这个形式，而不是「先来的保留原名」：工具集按字典遍历构建，顺序不确定，
    /// 谁先来谁保留会让同一个名字在两次重建之间指向不同的 Agent。后缀取自 Agent 自己的 Id，所以名字是稳定的。
    /// </remarks>
    public static string Disambiguate(string name, Guid agentId)
    {
        var suffix = "_" + Disambiguator(agentId);
        var room = MaxAgentToolNameLength - suffix.Length;
        return (name.Length > room ? name[..room] : name) + suffix;
    }

    private static string Disambiguator(Guid agentId) => agentId.ToString("N")[..DisambiguatorLength];
}
