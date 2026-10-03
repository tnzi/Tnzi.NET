using System.Net.Sockets;

namespace Tnzi.Identity.Services;

/// <summary>
/// 登录 IP 允许列表的算术部分：把操作员的自由文本解析成条目，判断一个客户端地址是否命中。
/// 无状态、无 I/O。
/// </summary>
/// <remarks>
/// <para>
/// <b>它是一个类，而且必须只有这一个。</b>管理员保存列表时跑的校验，与有人登录时跑的判定，
/// 对「允许」的理解必须逐字一致。两份实现在被修改之前都是一致的，而分叉的方向是无声的：
/// 一份保存得好好的列表，登录时谁也进不来，或者谁都进得来。
/// </para>
/// <para>
/// <b>IPv4 映射的 IPv6 地址</b>（<c>::ffff:203.0.113.5</c>）在比较前折回纯 IPv4。同一个客户端
/// 经不同的代理跳数到达时，两种形状都可能出现；不折叠的话，一条按 <c>203.0.113.5</c> 录入的
/// 规则在一套部署上命中、在另一套上不命中，<b>而症状是「密码错误」</b>。
/// </para>
/// </remarks>
public static class SignInIpAllowList
{
    private static readonly char[] Separators = ['\n', '\r', ',', ';'];

    /// <summary>把列表文本切成去掉首尾空白的条目，跳过空行与 <c>#</c> 开头的注释。</summary>
    public static IReadOnlyList<string> ParseEntries(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        return text
            .Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(entry => !entry.StartsWith('#'))
            .ToList();
    }

    /// <summary>条目是一个格式正确的精确地址或 CIDR 段。</summary>
    /// <remarks>
    /// 比 <see cref="IPAddress.TryParse(string, out IPAddress)"/> 严：那个方法接受 <c>203.0.113</c>
    /// 这种残缺的点分写法并把它读成 <c>203.0.0.113</c>。一条少敲了一段的地址在这里是拒绝，
    /// 不是安静地放行另一台机器。
    /// </remarks>
    public static bool IsValidEntry(string entry)
    {
        Check.NotNull(entry);
        var trimmed = entry.Trim();

        if (trimmed.Contains('/'))
        {
            return IPNetwork.TryParse(trimmed, out var network)
                && IsCompleteIfIPv4(network.BaseAddress, trimmed[..trimmed.IndexOf('/')]);
        }

        return IPAddress.TryParse(trimmed, out var address) && IsCompleteIfIPv4(address, trimmed);
    }

    /// <summary>IPv4 必须写满四段；IPv6 有自己的缩写规则，不在此列。</summary>
    private static bool IsCompleteIfIPv4(IPAddress parsed, string written)
        => parsed.AddressFamily != AddressFamily.InterNetwork || written.Count(c => c == '.') == 3;

    /// <summary>列表文本里所有格式不正确的条目，按出现顺序；全部合法时为空。</summary>
    public static IReadOnlyList<string> FindInvalidEntries(string? text)
        => ParseEntries(text).Where(entry => !IsValidEntry(entry)).ToList();

    /// <summary>
    /// 客户端地址至少命中一个条目。
    /// </summary>
    /// <remarks>
    /// 空白的客户端地址、解析不了的地址、空条目集在<b>这里</b>一律不命中。空列表意味着什么由调用方决定：
    /// 登录守卫把它读成「不限制」，写入服务拒绝在开着开关的情况下存一个。这个方法只回答「命中没有」。
    /// </remarks>
    public static bool IsAllowed(string? clientIp, IEnumerable<string> entries)
    {
        Check.NotNull(entries);

        if (string.IsNullOrWhiteSpace(clientIp) || !IPAddress.TryParse(clientIp, out var raw))
        {
            return false;
        }

        var address = Normalise(raw);
        return entries.Any(entry => Matches(address, entry));
    }

    private static bool Matches(IPAddress clientIp, string entry)
    {
        var trimmed = entry.Trim();

        if (!trimmed.Contains('/'))
        {
            return IPAddress.TryParse(trimmed, out var single) && Normalise(single).Equals(clientIp);
        }

        if (!IPNetwork.TryParse(trimmed, out var network))
        {
            return false;
        }

        // 网段与客户端各自折回规范形状后，只在同一地址族内比较。v4 映射的 v6 网段
        // （::ffff:203.0.113.0/120）折成等价的 v4 网段；除此之外地址族不同一律不命中 ——
        // 一个原生 IPv6 地址不在任何 IPv4 网段里，反之亦然。不能用 MapToIPv4 去「对齐」：
        // 它不校验地址是不是 v4 映射，直接取低 32 位，于是 2001:db8::cb00:7105 会被读成
        // 203.0.113.5，而 IPv6 的接口 ID 是客户端自己选的。
        var normalised = Normalise(network);
        return normalised.BaseAddress.AddressFamily == clientIp.AddressFamily && normalised.Contains(clientIp);
    }

    /// <summary>IPv4 映射的 IPv6 折回纯 IPv4；其它原样返回。</summary>
    private static IPAddress Normalise(IPAddress address)
        => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

    /// <summary>
    /// v4 映射前缀之内的 v6 网段折回等价的 v4 网段；其它原样返回。
    /// 映射的基地址意味着前缀至少 96 位（更短的前缀会把 <c>ffff</c> 那 16 位也掩掉，基地址就不再是映射形状）。
    /// </summary>
    private static IPNetwork Normalise(IPNetwork network)
        => network.BaseAddress.IsIPv4MappedToIPv6 && network.PrefixLength >= 96
            ? new IPNetwork(network.BaseAddress.MapToIPv4(), network.PrefixLength - 96)
            : network;
}
