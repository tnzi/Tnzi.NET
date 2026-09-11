namespace Tnzi.Notification.Push.Services.Internal;

/// <summary>
/// 匿名设备密钥的签发与形态校验。
/// </summary>
/// <remarks>
/// <para>
/// ★★ <b>为什么要校验客户端带来的密钥的形态。</b>刷新路径必须接受一个<b>查不到对应行</b>的密钥
/// （行被退役后要能重建，那是 <c>AnonymousDeviceId</c> 存在的全部理由），于是「这枚密钥是不是
/// 本服务签发的」在库里查不出来。不加校验的话，服务端就会原样接受客户端塞过来的<b>任何</b>字符串
/// —— 而最可能出现的形态不是攻击，是<b>图省事</b>：客户端开发者为了不写「存密钥」那一行，
/// 直接把 <c>identifierForVendor</c>、设备序列号之类现成的值填进请求头。
/// </para>
/// <para>
/// 那样一来，「服务端签发所以熵有保证」这条就整个落空了，而且<b>没有任何症状</b>：注册成功、
/// 推送正常，只是这台设备的全部凭据变成了一个会流进业务表、日志与支持工单的公开值，
/// 谁读到都能把它的推送地址改挂走。
/// </para>
/// <para>
/// ★ <b>这道校验挡的是什么、不挡什么。</b>它挡住「顺手拿一个现成的公开标识来用」——
/// UUID、序列号、邮箱、递增数字的形态都通不过。它<b>挡不住</b>刻意构造一个 43 位的低熵串
/// （比如 43 个 <c>a</c>），那需要有人明知故犯。这与口令复杂度规则是同一种东西：
/// 它提高的是下限，不是保证。
/// </para>
/// <para>
/// ★ <b>刻意不做成「校验失败就当没带密钥、直接签发一枚新的」。</b>那会让一个用错方式的客户端
/// 每次启动都拿到一个新身份，此前那些记录的回执一路断掉，而它收到的每个响应都是 200。
/// 就地拒绝并说清楚，才有人会去修客户端。
/// </para>
/// </remarks>
internal static class AnonymousDeviceKey
{
    /// <summary>
    /// 一枚签发出来的密钥的字符长度。
    /// </summary>
    /// <remarks>
    /// <see cref="OneTimeToken.DefaultEntropyBytes"/> 个字节经无填充 base64url 编码的结果长度。
    /// 由 <c>AnonymousDeviceKeyTests</c> 对着真实签发结果断言，不靠这里算得对。
    /// </remarks>
    internal const int Length = 43;

    /// <summary>签发一枚新的设备密钥。</summary>
    internal static string Issue() => OneTimeToken.Create();

    /// <summary>
    /// 这个值的形态是否与本服务签发出来的一致。
    /// </summary>
    /// <remarks>
    /// base64url 无填充：<c>[A-Za-z0-9_-]</c>，定长。<b>不接受</b>填充符 <c>=</c> 与
    /// 标准 base64 的 <c>+</c> <c>/</c> —— 签发端不会产出它们，出现即说明这个值另有来源。
    /// </remarks>
    internal static bool IsWellFormed(string? key)
    {
        if (key is not { Length: Length })
            return false;

        foreach (var c in key)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_')
                return false;
        }

        return true;
    }
}
