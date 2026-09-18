namespace Tnzi.AspNetCore.Security;

/// <summary>
/// 人机验证的统一入口：按配置挑出生效的 <see cref="ICaptchaProvider"/>，执行跨提供商的策略
/// （未启用即放行、用途绑定、主机名核对、评分阈值、验证服务不可达的处置），给出结论。
/// </summary>
/// <remarks>
/// <para>
/// <b>按需启用</b>：没配 <c>AspNetCore:Captcha:Provider</c> 时 <see cref="IsEnabled"/> 为 false，
/// <see cref="VerifyAsync"/> 一律放行（<see cref="CaptchaVerification.Skipped"/>）。这是刻意的 ——
/// 一个消费方给端点挂了 <c>[RequireCaptcha]</c> 却没配提供商，框架不能让它的整条路由 500；
/// 代价是这种「挂了特性没配提供商」的端点没有任何保护，所以启动期会把它们逐个记 Warning
/// （<see cref="EnsureConfigured"/>）。
/// </para>
/// <para>
/// <b>配了却没实现则拒绝启动</b>：<c>Provider</c> 指名的实现没注册，<see cref="EnsureConfigured"/> 抛
/// <see cref="ConfigurationException"/>。静默退回别的提供商会让配置、日志、接口全都正常而验证码换了一家，
/// 与 <c>Storage.Cloud</c> 配成 S3 却未加载时抛异常是同一条原则。
/// </para>
/// </remarks>
public interface ICaptchaVerifier
{
    /// <summary>是否配置了生效的提供商。</summary>
    bool IsEnabled { get; }

    /// <summary>生效的提供商名；未启用为 null。</summary>
    string? ProviderName { get; }

    /// <summary>
    /// 校验客户端交上来的令牌。
    /// </summary>
    /// <param name="token">令牌；null 或空白按 <see cref="CaptchaFailure.MissingToken"/> 拒绝（未启用时仍放行）。</param>
    /// <param name="purpose">端点声明的用途，同一枚令牌不能跨用途复用。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<CaptchaVerification> VerifyAsync(string? token, string purpose, CancellationToken cancellationToken = default);

    /// <summary>
    /// 浏览器渲染控件所需的公开配置。未启用时只有 <c>Enabled = false</c>。
    /// </summary>
    CaptchaClientConfigDto GetClientConfig();

    /// <summary>
    /// 启动期自检：配置指名的提供商必须已注册，否则抛 <see cref="ConfigurationException"/>。
    /// 由 AspNetCore 模块在应用初始化阶段调用一次。
    /// </summary>
    void EnsureConfigured();
}
