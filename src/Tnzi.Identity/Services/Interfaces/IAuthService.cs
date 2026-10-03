
namespace Tnzi.Identity.Services;

/// <summary>
/// 认证服务接口
/// </summary>
public interface IAuthService
{
    /// <summary>
    /// 登录
    /// </summary>
    Task<Result<string>> LoginAsync(LoginDto input);

    /// <summary>
    /// 登录（返回Token结果，包含RefreshToken）
    /// </summary>
    Task<Result<TokenResult>> LoginWithRefreshTokenAsync(LoginDto input);

    /// <summary>
    /// 刷新Token
    /// </summary>
    Task<Result<TokenResult>> RefreshTokenAsync(string refreshToken);

    /// <summary>
    /// 登出
    /// </summary>
    Task<Result<string>> LogoutAsync(Guid userId);

    /// <summary>
    /// 发送2FA验证码（登录时）
    /// </summary>
    Task<Result<TwoFactorChallengeDto>> SendTwoFactorCodeAsync(SendTwoFactorCodeDto input);

    /// <summary>
    /// 验证2FA并登录
    /// </summary>
    Task<Result<TokenResult>> VerifyTwoFactorAndLoginAsync(VerifyTwoFactorDto input);

    /// <summary>
    /// 用 passkey 完成两步验证，第一步：按临时令牌指明的账号生成断言选项。
    /// </summary>
    /// <remarks>
    /// 只在登录挑战确实提供了 <see cref="TwoFactorType.Passkey"/> 时给出选项；
    /// 用户单独关掉的方式在这里也一样拒绝，与发码 / 验码两条路同一口径。
    /// </remarks>
    Task<Result<PasskeyOptionsDto>> BeginTwoFactorPasskeyAsync(TwoFactorPasskeyBeginDto input);

    /// <summary>
    /// 用 passkey 完成两步验证，第二步：校验断言并登录。
    /// </summary>
    /// <remarks>
    /// ★ 断言证明的必须是临时令牌那个账号：拿别人的 passkey 也能得到一个成功的断言，
    /// 少了这一比，任何持有自己 passkey 的人都能替一个猜对了密码的账号完成第二步。
    /// 后半段与 <see cref="VerifyTwoFactorAndLoginAsync"/> 逐字相同：烧令牌 → 登录守卫 → 会话 → 令牌。
    /// </remarks>
    Task<Result<TokenResult>> VerifyTwoFactorWithPasskeyAndLoginAsync(TwoFactorPasskeyCompleteDto input);

    /// <summary>
    /// 对一个<strong>身份已经校验通过</strong>的用户完成签发：登录守卫（含账号锁定 / 停用）→
    /// 2FA 判定 → 会话协调器 → 带 <c>session_id</c> 的令牌 → 登录成功事件。
    /// </summary>
    /// <param name="user">已通过身份校验的用户。</param>
    /// <param name="method">触发本次签发的登录方式，传给登录守卫与登录日志。</param>
    /// <param name="satisfiedFactor">
    /// 本次身份校验<strong>已经证明</strong>的那个因子，会从 2FA 挑战列表里扣除。
    /// 例：邮箱验证码登录传 <see cref="TwoFactorType.Email"/> —— 用户刚刚证明了自己能收这个邮箱，
    /// 再问一次邮箱验证码问的是同一件事。传 null 表示本次登录不满足任何 2FA 因子（密码登录即如此）。
    /// </param>
    /// <remarks>
    /// <para>
    /// ★★ <strong>给"凭据校验不在 AuthService 里"的登录方式用的唯一出口。</strong>
    /// 首个消费者是 passkey（<see cref="IPasskeyService"/>）：WebAuthn 的断言由运行时校验，
    /// 但校验通过之后的每一步都必须与密码登录完全一致，否则 IP 白名单、多设备策略、
    /// 会话绑定、登录日志会在这条新路径上集体失效。
    /// </para>
    /// <para>
    /// <strong>调用它就等于声明"我已经确认这个人是谁了"</strong> —— 它不做任何凭据校验。
    /// 别拿它绕开密码校验。
    /// </para>
    /// <para>
    /// ★ <strong>账号锁定 / 停用照常挡</strong>：登录守卫这一步包含框架内置的
    /// <see cref="LockedAccountLoginGuard"/>。那道检查在密码路径上是
    /// <c>SignInManager.CheckPasswordSignInAsync</c> 顺手做掉的，凭据校验挪到别处就会跟着消失，
    /// 所以它被收口成了守卫 —— 一处实现覆盖全部签发路径，新增登录方式自动受保护。
    /// </para>
    /// <para>
    /// 2FA 照常判定：passkey 通过之后如果该用户还开着 2FA，一样会收到挑战。
    /// 框架不替部署方决定"passkey 是否已经算两个因子"。
    /// </para>
    /// <para>
    /// ★★ <paramref name="satisfiedFactor"/> 解决的是<strong>同一个因子被问两遍</strong>：
    /// 邮箱验证码登录的账号若把邮箱设为 2FA 方式，扣除之后集合为空即直接放行（不冗余）；
    /// 若还开着 TOTP，扣除邮箱后 TOTP 仍在集合里，照样挑战（不绕过）。
    /// 一次登录只能满足它<strong>实际证明过</strong>的那个因子，绝不能拿来整体跳过 2FA。
    /// </para>
    /// </remarks>
    Task<Result<TokenResult>> IssueTokenAsync(User user, LoginMethod method, TwoFactorType? satisfiedFactor = null);

    #region 验证码登录

    /// <summary>
    /// 发送验证码登录验证码
    /// </summary>
    /// <param name="input">发送请求（邮箱或手机号 + 类型）</param>
    /// <returns>操作结果</returns>
    Task<Result<string>> SendCodeLoginCodeAsync(SendCodeLoginCodeDto input);

    /// <summary>
    /// 验证码登录（首次登录如果用户不存在且快速注册开启则自动注册）
    /// </summary>
    /// <param name="input">登录请求（邮箱/手机号 + 验证码 + 类型）</param>
    /// <returns>登录结果（包含 Token 和是否需要设置密码）</returns>
    Task<Result<CodeLoginResultDto>> CodeLoginAsync(CodeLoginDto input);

    #endregion

    #region 验证码找回密码

    /// <summary>
    /// 发送密码找回验证码
    /// </summary>
    /// <param name="input">发送请求（邮箱或手机号 + 类型）</param>
    /// <returns>操作结果</returns>
    Task<Result<string>> SendPasswordRecoveryCodeAsync(SendPasswordRecoveryCodeDto input);

    /// <summary>
    /// 验证码重置密码
    /// </summary>
    /// <param name="input">重置请求（邮箱/手机号 + 验证码 + 新密码 + 类型）</param>
    /// <returns>操作结果</returns>
    Task<Result<string>> ResetPasswordByCodeAsync(ResetPasswordByCodeDto input);

    #endregion

    #region 认证配置

    /// <summary>
    /// 获取公开认证配置（登录方式 / 注册 / 找回 / 第三方登录的开关），供登录页按部署配置渲染。
    /// 只读现有配置选项，仅返回布尔开关与已启用的第三方提供商，不含任何密钥。
    /// </summary>
    /// <returns>认证配置</returns>
    Result<AuthConfigDto> GetAuthConfig();

    #endregion
}
