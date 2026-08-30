namespace Tnzi.Identity.Extensions;

/// <summary>
/// 联系方式脱敏：把验证码实际发往的地址显示成「看得出是哪个、但读不全」的形式。
/// </summary>
/// <remarks>
/// 用在发码回执上（「验证码已发送到 a***@example.com」）。★ 收口成一处而不是各流程各写一份 ——
/// 掩码规则一旦在某一处漂了，症状是<strong>某个流程比别处多露出几位</strong>，
/// 而它既不会编译失败也不会让任何测试变红。
/// </remarks>
internal static class ContactAddressMasking
{
    /// <summary>邮箱脱敏：只保留首字符与域名（<c>alice@example.com</c> → <c>a***@example.com</c>）。</summary>
    public static string? MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;
        var at = email.IndexOf('@');
        if (at <= 0) return "***";
        var name = email[..at];
        var domain = email[at..]; // 含 '@'
        var visible = name.Length <= 1 ? name : name[..1];
        return $"{visible}***{domain}";
    }

    /// <summary>手机号脱敏：仅保留末 4 位（<c>+14155552671</c> → <c>•••••2671</c>）。</summary>
    public static string? MaskPhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return null;
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.Length <= 4) return "••••";
        return $"•••••{digits[^4..]}";
    }

    /// <summary>按渠道选择对应的脱敏方式。</summary>
    public static string? Mask(string? address, TwoFactorType type)
        => type == TwoFactorType.Email ? MaskEmail(address) : MaskPhone(address);
}
