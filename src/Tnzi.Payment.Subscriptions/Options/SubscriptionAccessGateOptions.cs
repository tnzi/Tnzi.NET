namespace Tnzi.Payment.Subscriptions.Options;

/// <summary>
/// 服务暂停时的访问门禁（<c>Payment:Subscription:AccessGate</c>）。默认关闭。
/// </summary>
/// <remarks>
/// <para>
/// 打开之后，计费账户处于 <see cref="BillingStanding.Suspended"/> 时，这个账户的人发来的已认证请求一律答
/// <b>402</b> <c>SUBSCRIPTION_SUSPENDED</c>（附带 <c>canManage</c>，前端据此决定显示补缴入口还是「请联系管理员」），
/// SignalR 的连接与方法调用同样被拒。下面这些永远放行：匿名端点、未认证的请求、内置放行路径（登录与会话、
/// 个人资料与两步验证、订阅与支付本身、外壳启动要读的几样）、<see cref="AllowedPaths"/>、
/// 标了 <c>[AllowWhenSuspended]</c> 的端点，以及平台超级管理员与持有 <see cref="BypassPermissions"/> 之一的用户。
/// </para>
/// <para>
/// ★ 刻意不是运行期设置：这是一道访问控制，能从管理端热关它就等于给「欠费照用」留了一扇不留痕迹的门。
/// </para>
/// </remarks>
[ConfigSection("Payment:Subscription:AccessGate")]
public class SubscriptionAccessGateOptions
{
    /// <summary>
    /// 内置放行路径（相对 API 前缀，按路径段匹配）。被暂停的账户也必须能登录、退出、管理自己的会话与两步验证、
    /// 看订阅与账单、付款换卡，外壳也要能启动起来显示暂停页面。
    /// </summary>
    public static IReadOnlyList<string> BuiltInAllowedPaths { get; } =
    [
        // 登录、刷新令牌、退出、两步验证、passkey、二次确认
        "auth",
        // 个人资料、会话、两步验证设置、密码
        "users/profile",
        // 付款本身：订阅、补缴、账户境况、绑卡、支付状态同步、账单与退款
        "subscriptions",
        "payment-methods",
        "payments",
        "billing-account",
        "invoices",
        "refunds",
        // 外壳启动：模块清单、当前用户的权限画像、能力协商、语言包、外观
        "admin/shell",
        "admin/function-authorization/access-profile",
        "capabilities",
        "localization",
        "appearance"
    ];

    /// <summary>是否启用门禁。默认 false：与引入门禁之前逐字相同。</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// 只看这个产品的订阅（<c>Subscription.ProductCode</c>）。空 = 账户下所有订阅合起来的境况
    /// （有一条正常就不算暂停）。
    /// </summary>
    public string? ProductCode { get; set; }

    /// <summary>
    /// 额外放行的路径。不以 <c>/</c> 开头的相对 API 前缀（如 <c>firm/contact</c>），以 <c>/</c> 开头的是应用内绝对路径
    /// （如 <c>/hubs/notifications</c>）。按路径段匹配：<c>firm</c> 放行 <c>/api/firm</c> 与 <c>/api/firm/x</c>，不放行 <c>/api/firms</c>。
    /// </summary>
    public List<string> AllowedPaths { get; set; } = [];

    /// <summary>
    /// 持有其中任意一个权限码的用户不受门禁约束（平台运维账号、客服）。平台超级管理员不需要列在这里，恒不受约束。
    /// </summary>
    public List<string> BypassPermissions { get; set; } = [];

    /// <summary>
    /// 账户境况在门禁里缓存多少秒（每个请求都查一次库太贵）。境况变化时由事件主动失效，这个时长只是兜底。0 = 不缓存。
    /// </summary>
    public int CacheSeconds { get; set; } = 30;
}
