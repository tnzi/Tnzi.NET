
/// <summary>
/// 完成「必须先改密码」这件待办。
/// </summary>
/// <remarks>
/// 刻意<b>不含</b>用户标识：改谁由临时令牌决定。带上 userId 就等于让一个匿名端点
/// 接受「替谁改」这个参数。
/// </remarks>
public class CompletePasswordChangeDto
{
    /// <summary>登录时随待办挑战一起发下来的临时令牌（10 分钟有效，一次性）。</summary>
    [Required]
    public string TempToken { get; set; } = null!;

    /// <summary>新密码。走与其它改密路径同一套强度校验与历史校验。</summary>
    [Required]
    public string NewPassword { get; set; } = null!;
}

/// <summary>
/// 完成一件需要验证码的待办（绑定验证器 / 确认邮箱）。
/// </summary>
public class CompletePendingActionCodeDto
{
    /// <summary>登录时随待办挑战一起发下来的临时令牌。</summary>
    [Required]
    public string TempToken { get; set; } = null!;

    /// <summary>验证器算出的一次性码，或收到的邮箱验证码。</summary>
    [Required]
    public string Code { get; set; } = null!;
}

/// <summary>
/// 还欠哪些事，以及办它们需要的材料。
/// </summary>
public class PendingActionChallengeDto
{
    /// <summary>还欠着的动作名，如 <c>["ChangePassword", "EnrollTotp"]</c>。</summary>
    public IReadOnlyList<string> RequiredActions { get; set; } = [];

    /// <summary>账号的登录名，供表单显示「正在为谁办」。</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>
    /// 欠着 <see cref="PendingUserActions.EnrollTotp"/> 时带上密钥与 <c>otpauth://</c> 地址。
    /// </summary>
    /// <remarks>
    /// 不带它，前端只知道「你得绑个验证器」却没有二维码可扫 —— 那一步就走不下去。
    /// </remarks>
    public TotpSetupDto? TotpSetup { get; set; }

    /// <summary>
    /// 欠着 <see cref="PendingUserActions.ConfirmEmail"/> 时带上掩码邮箱，供提示「码发到哪」。
    /// </summary>
    public string? MaskedEmail { get; set; }
}

/// <summary>
/// 办完一件待办之后的结果。
/// </summary>
public class PendingActionResultDto
{
    /// <summary>是否全部办完。为 false 时账号仍进不去。</summary>
    public bool Completed { get; set; }

    /// <summary>还剩下哪些没办。</summary>
    public IReadOnlyList<string> RemainingActions { get; set; } = [];

    /// <summary>全部办完时直接给出的登录令牌，否则为 null。</summary>
    public TokenResult? Token { get; set; }
}
