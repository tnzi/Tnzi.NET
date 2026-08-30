namespace Tnzi.Identity.Dtos;

/// <summary>
/// 一次二次确认的结果。
/// </summary>
/// <remarks>
/// ★ <b>这里没有令牌。</b>确认记录留在服务端，客户端拿到的只是「成功了、有效到几点」——
/// 发一个客户端持有的凭据回去，等于又造出一样可以被偷走的东西，而它本来是不必存在的。
/// </remarks>
public class StepUpGrantDto
{
    /// <summary>本次确认覆盖的范围。</summary>
    public string Scope { get; set; } = null!;

    /// <summary>有效期截止时间（UTC）。</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>是否只能用一次。</summary>
    public bool SingleUse { get; set; }
}

/// <summary>
/// 请求发送二次确认验证码的入参。
/// </summary>
public class SendStepUpCodeDto
{
    /// <summary>渠道（Email / Sms）。TOTP 由验证器生成，不需要发码。</summary>
    public TwoFactorType Type { get; set; } = TwoFactorType.Email;
}

/// <summary>
/// 用验证码完成二次确认的入参。
/// </summary>
public class StepUpCodeDto
{
    /// <summary>验证码。</summary>
    public string Code { get; set; } = null!;

    /// <summary>验证码渠道。</summary>
    public TwoFactorType Type { get; set; } = TwoFactorType.Totp;

    /// <summary>确认范围，与受保护端点上声明的一致。</summary>
    public string Scope { get; set; } = null!;
}

/// <summary>
/// 用 passkey 完成二次确认的入参。
/// </summary>
public class StepUpPasskeyDto : PasskeyCompleteDto
{
    /// <summary>确认范围，与受保护端点上声明的一致。</summary>
    public string Scope { get; set; } = null!;
}
