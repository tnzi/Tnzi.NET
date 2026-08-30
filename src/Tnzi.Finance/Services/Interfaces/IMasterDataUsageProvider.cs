namespace Tnzi.Finance.Services;

/// <summary>
/// 一条主数据记录正被会计单据之外的东西使用的事实。
/// </summary>
/// <param name="Detail">面向操作员的说明：**是什么**在用它，以及该改做什么（通常是"停用而不是删除"）</param>
/// <remarks>
/// 刻意不带 wire 原因码：本框架里这个答案唯一的去处是删除端点的 409 消息，
/// 而核心自带的那几条守卫也一样只返回一句话。等到真的有呈现端要按原因分支时，
/// 再给这个 record 加一个属性即可（positional record 追加属性不破坏既有调用方）。
/// </remarks>
public sealed record MasterDataUsage(string Detail);

/// <summary>
/// 回答"这条主数据，除了会计单据之外还有别的东西在用吗？"
/// </summary>
/// <remarks>
/// <b>为什么存在</b>：客户 / 供应商 / 目录项的删除守卫必须知道"这条记录还有没有人引用"，
/// 而**引用者不一定在会计内核里**。报价单行与采购订单行就是这样的引用者：它们住在
/// <c>Tnzi.Finance.Offers</c>，而那个模块单向依赖核心。让 <c>CustomerService.DeleteAsync</c>
/// 直接查 <c>EstimateLine</c>，等于会计内核反向依赖要约模块，那个模块就永远拆不出去。
/// 手法与 <see cref="IJournalLineHoldProvider"/> 完全一致。
/// <br/><br/>
/// <b>语义</b>：**只读、只回答、不代为清理**。守卫拒绝时一律指路让操作员自己去处理引用
/// （通常是把主数据停用 <c>IsActive=false</c> 而不是删除）——替他把引用悄悄改掉，是在无声地
/// 篡改别人的单据。
/// <br/><br/>
/// <b>可选</b>：未注册任何实现时视为"没有别人在用"，全部路径回到引入本契约之前的行为。
/// 这是**唯一安全的缺省**：本契约只会**增加**拒绝，不会放宽任何既有守卫 —— 核心自己那几项
/// 检查（发票 / 账单 / 费用 / 贷项 / 收付款 / 单据行）永远先跑，且与本契约无关。
/// <br/><br/>
/// 多个实现可并存（<c>IEnumerable</c> 注入），例如消费应用自己的合同台账也引用同一批客户。
/// 任意一个回答"在用"即拒绝，第一个非 null 的答案就是给操作员的说明。
/// </remarks>
public interface IMasterDataUsageProvider
{
    /// <summary>
    /// 这条主数据被本实现所辖的记录引用了吗？
    /// </summary>
    /// <param name="kind">主数据种类；实现遇到自己不管的种类应直接返回 <c>null</c></param>
    /// <param name="id">主数据 Id</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>被引用时返回可展示的说明；未被引用（或本实现不管这个种类）返回 <c>null</c></returns>
    /// <remarks>
    /// 入参是**单条**记录，因此实现应当用存在性查询（<c>AnyAsync</c>）作答，
    /// 不要把引用者全集物化出来 —— 那个集合随经营年限只增不减。
    /// </remarks>
    Task<MasterDataUsage?> FindUsageAsync(FinanceMasterDataKind kind, Guid id, CancellationToken cancellationToken = default);
}
