namespace Tnzi.Payment.Subscriptions.Services;

/// <summary>
/// 服务暂停时的访问判定：一个用户此刻能不能用这个应用。HTTP 中间件与 SignalR 过滤器都只问它。
/// </summary>
public interface ISubscriptionAccessGate
{
    /// <summary>
    /// 用户 <paramref name="userId"/> 的请求是否放行。门禁关着、他不代表任何账户、账户没有暂停、他是平台超级管理员、
    /// 或持有某个绕过权限码时放行；否则拒绝，并带上他能否为账户付款（前端据此显示补缴入口还是「请联系管理员」）。
    /// </summary>
    Task<SubscriptionAccessDecision> EvaluateAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 用户 <paramref name="userId"/> 所代表账户此刻的境况（不走缓存），并顺带刷新门禁缓存：
    /// 补缴之后外壳读到正常的同一时刻，门禁也不再拦他。
    /// </summary>
    Task<BillingStandingDto> GetStandingAsync(Guid userId, string? productCode = null, CancellationToken cancellationToken = default);
}

/// <summary>门禁对一次访问的判定。</summary>
/// <param name="Allowed">是否放行。</param>
/// <param name="Standing">账户境况（放行的原因不是境况时也照实给出）。</param>
/// <param name="CanManage">被拒绝时：他能否为账户付款、换卡。</param>
public sealed record SubscriptionAccessDecision(bool Allowed, BillingStanding Standing, bool CanManage)
{
    /// <summary>放行（与境况无关的原因：门禁关着、没有账户）。</summary>
    public static SubscriptionAccessDecision Pass { get; } = new(true, BillingStanding.None, false);
}
