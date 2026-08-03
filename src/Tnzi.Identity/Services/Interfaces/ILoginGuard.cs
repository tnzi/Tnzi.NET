namespace Tnzi.Identity.Services;

/// <summary>
/// 触发本次令牌签发的登录方式（供 <see cref="ILoginGuard"/> 按方式区别对待）。
/// </summary>
public enum LoginMethod
{
    /// <summary>用户名/邮箱/手机号 + 密码</summary>
    Password = 0,

    /// <summary>密码登录并同时签发刷新令牌</summary>
    PasswordWithRefreshToken = 1,

    /// <summary>短信/邮箱验证码登录（免密）</summary>
    VerificationCode = 2,

    /// <summary>二次验证通过后的令牌签发</summary>
    TwoFactor = 3,

    /// <summary>第三方 OAuth 登录</summary>
    OAuth = 4,

    /// <summary>注册后自动登录</summary>
    Registration = 5,
}

/// <summary>
/// 登录守卫的求值上下文：身份**已经校验通过**，但令牌尚未签发、会话尚未建立。
/// </summary>
/// <param name="User">已通过身份校验的用户</param>
/// <param name="Method">触发本次签发的登录方式</param>
/// <param name="IpAddress">客户端 IP（取自 <c>IScopedContext</c>，可能为 null）</param>
/// <param name="UserAgent">客户端 User-Agent（可能为 null）</param>
public sealed record LoginGuardContext(
    User User,
    LoginMethod Method,
    string? IpAddress,
    string? UserAgent);

/// <summary>
/// 登录守卫的裁决结果。
/// </summary>
/// <remarks>
/// <para>
/// <b>★ 默认用 <see cref="DenyAsInvalidCredentials"/>，不要用 <see cref="Deny"/>。</b>
/// 守卫跑在密码校验**之后**，所以「守卫拒绝」这个响应本身就证明了口令是对的。
/// 若守卫返回一个与凭据错误可区分的结果（不同状态码 / 不同文案），攻击者从任意位置
/// 都能拿它当**口令预言机**枚举密码——而 IP 白名单、设备绑定这类守卫的全部意义
/// 恰恰是「即使口令泄露也进不来」。返回同形结果才不会把这个前提拆掉。
/// </para>
/// <para>
/// <see cref="Deny"/> 只适用于「告知本身无害」的场景（例如账号已被管理员停用，
/// 该事实用户从别处也能知道）。用它就是在明确接受上述泄露，故命名不做粉饰。
/// </para>
/// </remarks>
public readonly record struct LoginGuardResult
{
    private LoginGuardResult(bool allowed, string? message, int code, string? errorCode, string? auditReason)
    {
        Allowed = allowed;
        Message = message;
        Code = code;
        ErrorCode = errorCode;
        AuditReason = auditReason;
    }

    /// <summary>是否放行</summary>
    public bool Allowed { get; }

    /// <summary>拒绝时返回给客户端的消息（放行时为 null）</summary>
    public string? Message { get; }

    /// <summary>拒绝时的 HTTP 状态码</summary>
    public int Code { get; }

    /// <summary>拒绝时的错误码</summary>
    public string? ErrorCode { get; }

    /// <summary>
    /// 写入登录失败事件的**真实**原因。与 <see cref="Message"/> 分开，
    /// 使「对外同形、对内可查」两件事同时成立：运维在登录日志里看得到是哪条守卫拦的。
    /// </summary>
    public string? AuditReason { get; }

    /// <summary>放行</summary>
    public static LoginGuardResult Allow() => new(true, null, 0, null, null);

    /// <summary>
    /// 拒绝，且对外与「用户名或密码错误」完全同形（400 + 同一文案），不泄露口令是否正确。
    /// 这是守卫拒绝的**推荐**形态，理由见 <see cref="LoginGuardResult"/> 的备注。
    /// </summary>
    /// <param name="auditReason">写进登录失败事件的真实原因（不返回给客户端）</param>
    public static LoginGuardResult DenyAsInvalidCredentials(string auditReason)
        => new(false, "Invalid username or password", 400, ErrorCodes.IDENTITY_INVALID_PASSWORD,
               Check.NotNullOrWhiteSpace(auditReason));

    /// <summary>
    /// 拒绝并如实告知原因。<b>会泄露「口令正确」这一事实</b>（守卫跑在密码校验之后），
    /// 仅在告知本身无害时使用；否则用 <see cref="DenyAsInvalidCredentials"/>。
    /// </summary>
    /// <param name="message">返回给客户端的消息</param>
    /// <param name="code">HTTP 状态码（默认 403）</param>
    /// <param name="errorCode">错误码</param>
    /// <param name="auditReason">写进登录失败事件的原因；缺省取 <paramref name="message"/></param>
    public static LoginGuardResult Deny(string message, int code = 403, string? errorCode = null, string? auditReason = null)
        => new(false, Check.NotNullOrWhiteSpace(message), code,
               errorCode ?? ErrorCodes.IDENTITY_ERROR, auditReason ?? message);
}

/// <summary>
/// 登录守卫：在身份校验通过之后、**令牌签发与会话建立之前**否决一次登录。
/// </summary>
/// <remarks>
/// <para>
/// 用于 IP 白名单、设备绑定、可登录时段、地理围栏这类「凭据之外」的准入策略。
/// 消费应用实现并注册（可注册多个），框架经 <see cref="ILoginGuardEvaluator"/>
/// 按 <see cref="Order"/> 升序执行，**首个拒绝即短路**。未注册任何实现时零开销。
/// </para>
/// <para>
/// <b>为什么必须是前置守卫，而不是在控制器里事后检查：</b>
/// 登录成功路径带着几个不可回滚的副作用——建立并持久化登录会话（多设备策略据此
/// 踢掉其它设备）、清零登录失败计数、发布登录成功事件（登录日志记为成功）。在拿到
/// 令牌之后再拒绝，这些副作用**已经发生**：合法设备被踢下线、暴力破解的失败计数被
/// 清零、审计留下一条本不该有的成功记录，而返回码还泄露了口令正确。守卫跑在这一切
/// 之前，拒绝时改为记一次登录失败，语义才是对的。
/// </para>
/// <para>
/// 守卫抛出的异常一律视为**拒绝**（fail-closed）：准入策略静默失效比登录失败危险得多。
/// </para>
/// </remarks>
public interface ILoginGuard
{
    /// <summary>执行顺序（升序，越小越先）。用于把开销低的守卫排在前面。</summary>
    int Order => 0;

    /// <summary>
    /// 裁决一次登录。放行返回 <see cref="LoginGuardResult.Allow"/>；
    /// 拒绝**优先**返回 <see cref="LoginGuardResult.DenyAsInvalidCredentials"/>。
    /// </summary>
    /// <param name="context">求值上下文（用户已通过身份校验）</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<LoginGuardResult> EvaluateAsync(LoginGuardContext context, CancellationToken cancellationToken = default);
}
