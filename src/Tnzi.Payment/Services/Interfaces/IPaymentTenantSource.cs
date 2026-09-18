namespace Tnzi.Payment.Services;

/// <summary>
/// 多租户开启时，支付后台循环要逐个切进去扫描的租户。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么存在</b>：<see cref="PaymentBackgroundService"/> 跑在没有请求上下文的 scope 里，当前租户为空，
/// 多租户开启时全局过滤器就成了 <c>TenantId IS NULL</c> —— 关过期支付、对账退款与订阅域的六条扫描
/// 每一轮都 <c>Processed 0</c>，零 Warning：过期单不关、券不还、续费不发生、逾期不过期。
/// 后台循环因此先问一遍「有哪些租户要扫」，再对每个租户 <c>ICurrentTenant.Change</c> 后跑全部扫描。
/// </para>
/// <para>
/// <b>贡献式</b>：以 <c>IEnumerable&lt;T&gt;</c> 解析并取并集。父模块贡献「有未终结支付 / 退款的租户」，
/// 续费域贡献「有存续订阅的租户」（只用试用、一笔支付都没有的租户不会出现在支付表里），
/// 消费方若有租户注册表可以直接贡献全量。方向是<b>宁可多扫不可漏扫</b>：多扫一个租户的代价是几条空查询，
/// 漏扫一个租户的代价是那个租户的钱与服务都停下来且没有任何迹象。
/// </para>
/// <para>
/// 只在多租户开启时被调用；实现里可以放心引用 <c>TenantId</c> 列（未开启时那一列被模型忽略，LINQ 翻译不了）。
/// 返回值里允许 <c>null</c>（宿主级 / 无租户的行）。
/// </para>
/// </remarks>
public interface IPaymentTenantSource
{
    /// <summary>
    /// 这一轮要扫的租户 Id（可含 <c>null</c>）。
    /// </summary>
    Task<IReadOnlyList<Guid?>> GetTenantIdsAsync(CancellationToken cancellationToken = default);
}
