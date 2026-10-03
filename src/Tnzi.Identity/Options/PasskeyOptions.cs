namespace Tnzi.Identity.Options;

/// <summary>
/// Passkey（WebAuthn）部署级配置。
/// </summary>
/// <remarks>
/// <para>
/// <strong>默认关闭。</strong>同 <c>Otp.EnableTotp</c> 那条的判据：不是每个消费应用都想要这条登录方式，
/// 而恒开会让它一直出现在个人中心和登录页上。开启是部署决策。
/// </para>
/// <para>
/// ★ <strong>框架只做接线，密码学与凭据存储都由运行时负责</strong>：
/// ASP.NET Core Identity 10 自带 <c>IPasskeyHandler&lt;TUser&gt;</c> 与
/// <c>IUserPasskeyStore&lt;TUser&gt;</c>（EF 侧 <c>UserStore</c> 已实现全部方法），
/// 不需要任何第三方 WebAuthn 库。
/// </para>
/// </remarks>
[ConfigSection("Identity:Passkey")]
public class PasskeyOptions
{
    /// <summary>
    /// 是否启用 passkey 登录与注册。默认 <c>false</c>。
    /// </summary>
    /// <remarks>
    /// 关闭时四个端点一律返回 400 <c>CONFIGURATION_ERROR</c>，与 TOTP 渠道关闭时的形态一致。
    /// </remarks>
    public bool Enabled { get; set; }

    /// <summary>
    /// Relying Party ID，即凭据绑定的域名（例如 <c>example.com</c>）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 留空则由运行时按当前请求的 host 推断，本地开发方便，<strong>生产应显式配置</strong>。
    /// </para>
    /// <para>
    /// ★ <strong>改这个值会让已注册的所有 passkey 失效</strong> —— 凭据在创建时就绑定了 RP ID，
    /// 换一个域名等于换了一个信赖方，浏览器不会把旧凭据交出来。它不是可以随手调的部署参数。
    /// </para>
    /// </remarks>
    public string? ServerDomain { get; set; }

    /// <summary>
    /// 一次挑战的存活秒数。默认 300（5 分钟），下限 30。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 用户要在这段时间内完成指纹/面容/PIN 交互。太短会让慢用户失败，太长则扩大重放窗口。
    /// </para>
    /// <para>
    /// ★ <strong>它同时喂给两侧</strong>：浏览器那侧（<c>IdentityPasskeyOptions.Timeout</c>，
    /// 系统弹窗等多久）与服务端那侧（缓存里挑战状态的 TTL）。只设一侧的话，用户会遇到
    /// "弹窗还开着、提交回来却说挑战已过期"这类查不出原因的失败。
    /// </para>
    /// </remarks>
    public int ChallengeTimeoutSeconds { get; set; } = 300;

    /// <summary>
    /// 是否要求用户验证（指纹 / 面容 / PIN），而不只是"持有这台设备"。默认 <c>true</c>。
    /// </summary>
    /// <remarks>
    /// 关掉它意味着捡到解锁状态设备的人就能登录，passkey 从"双因子"退化成"单因子"。
    /// 只有在另有一层因子把关时才应该关。
    /// </remarks>
    public bool RequireUserVerification { get; set; } = true;

    /// <summary>
    /// 登记时是否要求把凭据存进认证器（可发现凭据 / resident key）：<c>discouraged</c> / <c>preferred</c> / <c>required</c>。
    /// 默认留空 = 运行时默认（<c>discouraged</c>）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 只影响<strong>之后</strong>登记的凭据。平台认证器（Windows Hello、iCloud 钥匙串）不管这里怎么写都存成可发现凭据；
    /// 差别在硬件安全密钥：YubiKey 按 <c>discouraged</c> 登记出来的凭据<strong>不可发现</strong>，
    /// 只有服务端已经知道是谁、把 <c>allowCredentials</c> 带上的流程（两步验证的第二步、二次确认、报了用户名的登录）才找得到它；
    /// 登录页上「不输账号直接插密钥」要它是可发现凭据，那就得 <c>preferred</c> 或 <c>required</c>
    /// （YubiKey 5 的可发现凭据有槽位上限，25 或 100 枚按型号）。
    /// </para>
    /// </remarks>
    public string? ResidentKey { get; set; }

    /// <summary>
    /// 登记时允许哪一类认证器：<c>platform</c>（本机的指纹 / 面容 / PIN）或 <c>cross-platform</c>（漫游的硬件安全密钥）。
    /// 默认留空 = 两类都允许。
    /// </summary>
    /// <remarks>
    /// 只影响之后的登记，已登记的凭据不受影响。设成 <c>cross-platform</c> 即「必须用 YubiKey 这类硬件密钥」；
    /// 框架不校验 attestation，所以这是<strong>唯一</strong>能把平台认证器挡在外面的开关。
    /// </remarks>
    public string? AuthenticatorAttachment { get; set; }

    /// <summary>
    /// 注册令牌的默认有效期（分钟）。默认 1440（一天）。
    /// </summary>
    /// <remarks>
    /// 签发时可以逐次覆盖。这个值只是"没特别指定时用多久"，
    /// 不是上限 —— 上限该由签发它的业务流程自己把关。
    /// </remarks>
    public int EnrollmentTokenLifetimeMinutes { get; set; } = 1440;
}
