namespace Tnzi.AI.Infrastructure.Mcp;

/// <summary>
/// MCP 工具名：服务器前缀的拼法，以及从（可能被包装过的）工具上取回服务器自己的原名。
/// </summary>
/// <remarks>
/// 前缀名要能直接交给模型：OpenAI / Anthropic 的工具名只收 <c>[a-zA-Z0-9_-]</c> 且不超过 64 个字符，
/// 所以拼成 <c>{server}__{tool}</c>，其它字符换成 <c>_</c>；超长时截断并接上全名的短哈希，
/// 两个只在尾部不同的长名字不会截成同一个。
/// </remarks>
public static class McpToolNames
{
    /// <summary>模型侧工具名的长度上限</summary>
    public const int MaxLength = 64;

    /// <summary>服务器名与工具名之间的分隔</summary>
    public const string Separator = "__";

    private const int HashLength = 8;

    /// <summary>加了服务器前缀的工具名（只含 <c>[a-zA-Z0-9_-]</c>，不超过 <see cref="MaxLength"/>）</summary>
    public static string Prefixed(string serverName, string toolName)
    {
        Check.NotNullOrWhiteSpace(serverName);
        Check.NotNullOrWhiteSpace(toolName);

        var name = Sanitize(serverName) + Separator + Sanitize(toolName);
        if (name.Length <= MaxLength)
        {
            return name;
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(serverName + "\n" + toolName)), 0, HashLength / 2).ToLowerInvariant();
        return name[..(MaxLength - HashLength - 1)] + "_" + hash;
    }

    /// <summary>
    /// 服务器给这个工具起的原名：穿过包装层找到 SDK 的 <see cref="McpClientTool"/>，读协议层的名字；
    /// 不是 MCP 工具时返回工具自己的名字。
    /// </summary>
    public static string? OriginalName(AITool tool)
        => tool.GetService<McpClientTool>()?.ProtocolTool.Name ?? tool.Name;

    private static string Sanitize(string value)
    {
        var chars = value.Trim().ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (!char.IsAsciiLetterOrDigit(chars[i]) && chars[i] != '_' && chars[i] != '-')
            {
                chars[i] = '_';
            }
        }
        return new string(chars);
    }
}
