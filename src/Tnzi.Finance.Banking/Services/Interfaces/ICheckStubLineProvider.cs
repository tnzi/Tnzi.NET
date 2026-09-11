namespace Tnzi.Finance.Banking.Services;

/// <summary>
/// 存根附加行的来源（由<b>消费应用</b>实现，依赖方向恒为 业务 → 框架）
/// </summary>
/// <remarks>
/// 框架知道一张支票付的是哪几笔付款单，但不知道那些付款在消费应用的域里代表什么
/// （案卷号、工单号、保单号）。这个契约就是那句提问：<b>「这几笔付款，存根上还该印什么？」</b>
/// <para>
/// ★ <b>为什么是提问而不是把行存进 <c>BankCheck</c></b>：域信息本来就在消费应用自己的库里，
/// 复制一份进框架的登记簿会立刻产生两个真值（案卷改名之后哪一份算数？），
/// 也会把框架的支票登记簿变成一张什么都装的杂物表。
/// 更关键的是——<b>不存反而让「重新渲染一张历史支票」这件事自动成立</b>：
/// <c>RenderAsync</c> / <c>ReprintAsync</c> 拿着同样的付款单 id 再问一次，答案还在。
/// 上一轮钉进 <c>BankCheck</c> 的是<b>版式</b>（模板 / 票纸 / 偏移），
/// 那些是账户设置、会被改、且改了之后无从回溯；两者不是一类东西，故一个存、一个问。
/// </para>
/// <para>
/// ★ <b>未注册 = 没有附加行</b>，存根按出厂样子排 —— 与本机制引入前逐字相同。
/// 这是可选扩展点唯一安全的缺省方向。
/// </para>
/// <para>
/// ★ 临时预览（<c>PreviewAdHocAsync</c>）<b>不经过本契约</b>：那条路径上还没有付款单，
/// 无键可查，故由请求 DTO 直接携带（见 <c>AdHocCheckItemDto.StubLines</c>）。
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public class MatterStubLineProvider : ICheckStubLineProvider
/// {
///     public async Task&lt;IReadOnlyDictionary&lt;Guid, IReadOnlyList&lt;CheckStubLine&gt;&gt;&gt; GetStubLinesAsync(
///         IReadOnlyList&lt;Guid&gt; paymentEntryIds, CancellationToken cancellationToken = default)
///     {
///         var rows = await _disbursements.AsNoTracking()
///             .Where(d =&gt; paymentEntryIds.Contains(d.PaymentEntryId))
///             .ToListAsync(cancellationToken);
///
///         return rows.ToDictionary(
///             d =&gt; d.PaymentEntryId,
///             d =&gt; (IReadOnlyList&lt;CheckStubLine&gt;)
///             [
///                 new CheckStubLine("File No.", d.FileNumber),
///                 new CheckStubLine("Client", d.ClientName)
///             ]);
///     }
/// }
/// </code>
/// 在消费应用的模块里 <c>context.Services.AddScoped&lt;ICheckStubLineProvider, MatterStubLineProvider&gt;()</c>。
/// </example>
public interface ICheckStubLineProvider
{
    /// <summary>
    /// 按付款单批量取存根附加行。
    /// </summary>
    /// <param name="paymentEntryIds">本次要印的付款单（已去重）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>
    /// 付款单 id → 该张支票的附加行。<b>不必</b>为每个 id 都给出条目：查不到的按「没有附加行」处理。
    /// </returns>
    /// <remarks>
    /// ★ 刻意是<b>批量</b>签名：一次批量付款可能开出几十张票，逐张回调就是一次 N+1
    /// （框架自己解析收款人档案时也是一次查询取全部，见 <c>CheckBatchComposer.LoadPayeesAsync</c>）。
    /// <para>
    /// ★ 实现<b>不得抛异常</b>作为「没有数据」的表达：这条路径上支票号已经分配、
    /// 登记簿行已经写下，一次意外抛出会让整批打印回滚 —— 拿不到装饰性的附加行
    /// 不该让付款打不出来。框架侧仍会兜住异常并降级为「无附加行」，但那是兜底不是许可。
    /// </para>
    /// </remarks>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<CheckStubLine>>> GetStubLinesAsync(
        IReadOnlyList<Guid> paymentEntryIds, CancellationToken cancellationToken = default);
}
