namespace Tnzi.AspNetCore.Security;

/// <summary>
/// 一次人机验证被拒绝的原因。
/// </summary>
/// <remarks>
/// 拒绝原因只进日志与响应的 <c>errorDetails</c>，给运维与前端分辨「用户没做」「做了但被判机器」
/// 「验证服务本身不可达」三类完全不同的处境；用户可见的消息一律是同一句「请完成人机验证」，
/// 不按原因区分 —— 那会变成给机器人调参的反馈信号。
/// </remarks>
public enum CaptchaFailure
{
    /// <summary>未被拒绝。</summary>
    None = 0,

    /// <summary>请求没有带验证令牌。</summary>
    MissingToken,

    /// <summary>提供商判定令牌无效（答错、伪造、密钥不匹配）。</summary>
    Rejected,

    /// <summary>令牌已过期或已被使用过一次。</summary>
    ExpiredOrReplayed,

    /// <summary>令牌绑定的动作（action / purpose）与本端点声明的用途不一致。</summary>
    ActionMismatch,

    /// <summary>令牌是在别的站点上解出来的（hostname 不在允许列表内）。</summary>
    HostnameMismatch,

    /// <summary>评分低于阈值（只有出评分的提供商会给出）。</summary>
    LowScore,

    /// <summary>
    /// 验证服务不可达（网络错误、超时、HTTP 5xx / 408 / 429）。其余非 2xx 是请求本身被拒，
    /// 归 <see cref="Rejected"/>，不受放行策略影响。默认按拒绝处理，
    /// 见 <see cref="CaptchaUnavailablePolicy"/>。
    /// </summary>
    VerifierUnavailable,

    /// <summary>
    /// 配置指名的提供商没有任何实现被注册。启动期就会拦下（<see cref="ICaptchaVerifier.EnsureConfigured"/>），
    /// 这里只是运行期的兜底。
    /// </summary>
    ProviderNotRegistered
}

/// <summary>
/// 验证服务不可达时的处置。
/// </summary>
public enum CaptchaUnavailablePolicy
{
    /// <summary>
    /// 拒绝请求（默认）。一枚在验证服务宕机期间静默放行的验证码，与「限流取不到分区键就放行」是同一类失效：
    /// 页面照样显示验证码、日志照样写「通过」，只是不再拦任何人。
    /// </summary>
    Deny = 0,

    /// <summary>
    /// 放行请求并记 Warning。适合把「登录可用」看得比「挡住机器人」更重的部署，
    /// 且必须清楚这段时间里的验证码等于没有。
    /// </summary>
    Allow
}

/// <summary>
/// 交给提供商去校验的一次提交。
/// </summary>
public sealed class CaptchaVerificationRequest
{
    /// <summary>
    /// 初始化请求。
    /// </summary>
    /// <param name="token">客户端拿到的验证令牌（各提供商的 response / payload / 通行令牌）。</param>
    /// <param name="purpose">本端点声明的用途，例如 <c>login</c> / <c>register</c> / <c>contact</c>。</param>
    /// <param name="remoteIp">客户端 IP，可为空；支持的提供商会把它一并交给验证服务。</param>
    public CaptchaVerificationRequest(string token, string purpose, string? remoteIp)
    {
        Token = Check.NotNullOrWhiteSpace(token);
        Purpose = Check.NotNullOrWhiteSpace(purpose);
        RemoteIp = remoteIp;
    }

    /// <summary>验证令牌。</summary>
    public string Token { get; }

    /// <summary>端点声明的用途。</summary>
    public string Purpose { get; }

    /// <summary>客户端 IP。</summary>
    public string? RemoteIp { get; }
}

/// <summary>
/// 一次人机验证的结论。
/// </summary>
public sealed class CaptchaVerification
{
    private CaptchaVerification(bool passed, string provider, CaptchaFailure failure, string? detail)
    {
        Passed = passed;
        Provider = provider;
        Failure = failure;
        Detail = detail;
    }

    /// <summary>是否通过。</summary>
    public bool Passed { get; }

    /// <summary>做出结论的提供商名；未启用验证时为空串。</summary>
    public string Provider { get; }

    /// <summary>被拒绝的原因；通过时为 <see cref="CaptchaFailure.None"/>。</summary>
    public CaptchaFailure Failure { get; }

    /// <summary>给日志看的补充说明（提供商回的 error-codes、异常消息等），不面向用户。</summary>
    public string? Detail { get; }

    /// <summary>提供商给出的评分（0 到 1，越高越像人）；不出评分的提供商为 null。</summary>
    public double? Score { get; private init; }

    /// <summary>令牌绑定的动作；提供商不回传时为 null。</summary>
    public string? Action { get; private init; }

    /// <summary>令牌被解出的站点主机名；提供商不回传时为 null。</summary>
    public string? Hostname { get; private init; }

    /// <summary>
    /// 复制本结论并附上提供商回传的 hostname / action / score（给日志与拒绝详情用）。
    /// </summary>
    public CaptchaVerification WithReport(string? hostname, string? action, double? score)
        => new(Passed, Provider, Failure, Detail) { Hostname = hostname, Action = action, Score = score };

    /// <summary>
    /// 是否是「验证未启用，放行」的结论。<see cref="Passed"/> 为 true 且 <see cref="Provider"/> 为空。
    /// </summary>
    public bool Skipped => Passed && Provider.Length == 0;

    /// <summary>通过。</summary>
    public static CaptchaVerification Pass(string provider, string? detail = null)
        => new(true, Check.NotNull(provider), CaptchaFailure.None, detail);

    /// <summary>拒绝。</summary>
    public static CaptchaVerification Fail(string provider, CaptchaFailure failure, string? detail = null)
    {
        Check.NotNull(provider);
        if (failure == CaptchaFailure.None)
            throw new ArgumentException("A failed verification must carry a failure reason.", nameof(failure));
        return new CaptchaVerification(false, provider, failure, detail);
    }

    /// <summary>未启用任何提供商，放行。</summary>
    public static CaptchaVerification NotEnabled() => new(true, string.Empty, CaptchaFailure.None, null);
}
