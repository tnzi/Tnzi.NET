namespace Tnzi.Notification.Metadata;

/// <summary>
/// 服务商键（provider key）：一条消息由哪一个已注册的发送器投递。
/// </summary>
/// <remarks>
/// <para>
/// 每条渠道（邮件 / 短信 / 推送 / 传真）可以注册多个发送器：一个<b>默认</b>的（普通 DI 注册，
/// 也就是注入 <c>IEmailSender</c> 拿到的那个）加任意多个<b>具名</b>的（keyed service，
/// 或 <c>Notification:MailSenders:{key}</c> 这类配置节）。消息通过 <c>Message.ProviderKey</c>
/// 指定用哪一个；没指定就走默认。
/// </para>
/// <para>
/// ★ <b>「default」是保留键</b>：它永远指向那个普通 DI 注册，配置节里不允许再定义一个同名 profile ——
/// 否则同一个键有两个来源，哪个生效取决于注册顺序，而两种结果看起来都是「发出去了」。
/// </para>
/// <para>
/// ★ 键不区分大小写，<b>规范形态是小写</b>：<c>Marketing</c> 与 <c>marketing</c> 是同一个 profile。
/// 配置节、请求、选择器返回值都经 <see cref="Normalize"/> 收口后再登记或落库，于是 keyed service 的键
/// （它按 <c>object.Equals</c> 比较，天然区分大小写）只有一种写法，「默认」也只有一种写法（<see langword="null"/>），
/// 按键统计与筛选才对得上。消费方在代码里注册 keyed sender 时走 <c>AddNotificationSender</c> 扩展即可，
/// 它替你收口；直接调 <c>AddKeyedScoped</c> 的话键要自己写成小写。
/// </para>
/// </remarks>
public static class NotificationProviderKeys
{
    /// <summary>默认发送器的保留键。</summary>
    public const string Default = "default";

    /// <summary>键的最大长度（与 <c>Message.ProviderKey</c> 的列宽同源）。</summary>
    public const int MaxLength = 64;

    /// <summary>
    /// 键的形状：字母或数字开头，之后允许字母、数字、<c>_</c>、<c>-</c>、<c>.</c>。
    /// </summary>
    /// <remarks>
    /// 收得紧是刻意的：键会出现在配置节路径、日志与失败原因里，空白与冒号会让
    /// <c>Notification:MailSenders:{key}</c> 这条路径本身歧义。
    /// </remarks>
    private static readonly Regex Shape = new("^[A-Za-z0-9][A-Za-z0-9_.-]*$", RegexOptions.Compiled);

    /// <summary>
    /// 把调用方给的键收口成规范形态：空白与 <see cref="Default"/>（不区分大小写）都归一为
    /// <see langword="null"/>，其余去掉首尾空白并转成小写。
    /// </summary>
    public static string? Normalize(string? providerKey)
    {
        if (string.IsNullOrWhiteSpace(providerKey))
            return null;

        var trimmed = providerKey.Trim();
        return IsDefault(trimmed) ? null : trimmed.ToLowerInvariant();
    }

    /// <summary>这个键指的是不是默认发送器（<see langword="null"/>、空白、或 <see cref="Default"/>）。</summary>
    public static bool IsDefault(string? providerKey)
        => string.IsNullOrWhiteSpace(providerKey)
           || string.Equals(providerKey.Trim(), Default, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 一个<b>已经 <see cref="Normalize"/> 过</b>的非空键是否合法（长度与形状）。
    /// </summary>
    public static bool IsValid(string providerKey)
    {
        Check.NotNull(providerKey);
        return providerKey.Length <= MaxLength && Shape.IsMatch(providerKey);
    }

    /// <summary>
    /// 校验失败时给调用方看的原因；合法则返回 <see langword="null"/>。
    /// </summary>
    public static string? Describe(string providerKey)
    {
        Check.NotNull(providerKey);

        if (providerKey.Length > MaxLength)
            return $"Provider key '{providerKey}' is longer than {MaxLength} characters.";

        if (!Shape.IsMatch(providerKey))
            return $"Provider key '{providerKey}' must start with a letter or digit and contain only letters, digits, '_', '-' or '.'.";

        return null;
    }
}
