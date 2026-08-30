namespace Tnzi.Finance.Offers.Metadata;

/// <summary>
/// 报价单 / 采购订单的生命周期状态（**不过账单据**共用）
/// </summary>
/// <remarks>
/// 与 <see cref="FinanceDocumentStatus"/> 刻意分开：那个枚举的每一档都在描述
/// 单据与总账的关系（已过账 / 已核销 / 已作废），而报价单和采购订单**从不触碰
/// 总账**——它们描述的是一次商业往来走到了哪一步。把两者塞进同一个枚举，会让
/// "Posted 的报价单" 这种无意义状态在类型上成立。
///
/// 两侧语义镜像：报价单的 Accepted = 客户接受了我方报价；采购订单的 Accepted =
/// 供应商确认了我方订单。
///
/// 随单据搬进本模块（原在核心 <c>Metadata/Enums.cs</c>）：它只描述本模块两类单据的
/// 状态，核心没有一处引用它。数值一字不变——它是持久化契约（<c>Status</c> 列的存储值），
/// 重排等于把全部存量单据的状态读错。
/// </remarks>
public enum FinanceOfferStatus
{
    /// <summary>草稿（可编辑、可删除、尚未占号）</summary>
    Draft = 0,

    /// <summary>已发出（分配编号；对方已经看到了这个号，故此后只能关闭不能删除）</summary>
    Sent = 1,

    /// <summary>对方已接受</summary>
    Accepted = 2,

    /// <summary>对方已拒绝</summary>
    Declined = 3,

    /// <summary>已转为正式单据（发票 / 账单），转换目标记录在 ConvertedTo* 字段</summary>
    Converted = 4,

    /// <summary>已关闭（过期、作罢、不再跟进）</summary>
    Closed = 5
}
