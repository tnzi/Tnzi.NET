namespace Tnzi.Payment.Services;

/// <summary>
/// 回答「这个用户此刻代表哪个计费账户、对它能做什么」。支付、绑卡、退款、订阅、发票的用户面操作都先问它。
/// </summary>
/// <remarks>
/// <para>
/// 内置实现 <see cref="DefaultBillingAccountResolver"/> 按 <c>Payment:BillingAccount:Scope</c> 三选一
/// （User / Installation / Tenant）。需要别的划分的消费方注册自己的实现：本模块以 <c>TryAddScoped</c> 注册内置实现，
/// 先注册的胜出。
/// </para>
/// <para>
/// ★ 权限在<b>服务层</b>判，不在控制器上：控制器上的权限码会让「每个用户给自己订阅」的消费方（默认的 User 范围）
/// 全部被挡在门外。User 范围下用户对自己的账户恒可查看、恒可管理，与引入计费账户之前逐字相同。
/// </para>
/// <para>
/// 只用于有人在场的请求。后台扣款、渠道回调、事件处理器不经它：那些路径上的账户取自记录本身
/// （订阅、支付行上的 <c>BillingAccountType</c> / <c>BillingAccountId</c>）。
/// </para>
/// </remarks>
public interface IBillingAccountResolver
{
    /// <summary>
    /// 用户 <paramref name="userId"/> 在当前请求上下文里代表的账户与他的权限；他不代表任何账户时返回 <see langword="null"/>
    /// （例如 Tenant 范围下一个不属于任何租户的宿主用户）。
    /// </summary>
    Task<BillingAccountAccess?> ResolveAsync(Guid userId, CancellationToken cancellationToken = default);
}
