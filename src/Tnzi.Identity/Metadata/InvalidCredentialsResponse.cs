namespace Tnzi.Identity.Metadata;

/// <summary>
/// 「用户名或密码错误」对外的唯一形状：状态码、文案、错误码三件在这里一起定义。
/// 密码路径（用户不存在 / 密码错误）与 <see cref="Services.LoginGuardResult.DenyAsInvalidCredentials"/>
/// 都从这里取值，不各自写字面量。
/// </summary>
/// <remarks>
/// <para>
/// ★★ 登录守卫跑在密码校验<b>之后</b>，所以「守卫拒绝」与「密码错误」的响应必须<b>逐字节相同</b>：
/// 状态码、文案、错误码、错误详情，差任何一个字段都够把端点变成口令预言机。此前差的正是错误码 ——
/// 守卫带 <c>IDENTITY_INVALID_PASSWORD</c>、密码错误为 <c>null</c>，文案与状态码看起来一样，
/// 而对一个开了登录 IP 允许列表的账号，从任意地址按 <c>errorCode</c> 就能枚举出正确密码。
/// 三件事定义在一处、两条路径引用同一处，回归锁在 <c>LoginGuardTests</c>：
/// 两条路径渲染成同一信封后逐字节比较。
/// </para>
/// </remarks>
public static class InvalidCredentialsResponse
{
    /// <summary>对外文案。用户不存在与密码错误共用它，刻意不区分。</summary>
    public const string Message = "Invalid username or password";

    /// <summary>HTTP 状态码。</summary>
    public const int StatusCode = 400;

    /// <summary>
    /// 错误码，<b>刻意为 null</b>。
    /// </summary>
    /// <remarks>
    /// 密码路径是所有消费方最常命中的失败响应，它从来不带错误码；给它加一个码就是改每个消费方
    /// 都在处理的 wire 形状，而守卫拒绝按定义不该被任何客户端区分对待，所以收敛到密码路径这一侧。
    /// 若将来要给凭据错误一个可本地化的码，两处必须同一次改，让 <c>LoginGuardTests</c> 的信封比较守着。
    /// </remarks>
    public const string? ErrorCode = null;
}
