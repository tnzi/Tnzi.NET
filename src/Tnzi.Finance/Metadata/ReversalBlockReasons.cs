namespace Tnzi.Finance.Metadata;

/// <summary>
/// 冲销受阻原因代码（<see cref="Dtos.ReversibilityDto.BlockedBy"/> 的取值）常量
/// </summary>
/// <remarks>
/// <para>
/// 这些代码是 <b>wire 契约</b>：进 API 响应、进呈现端的分支判断（按代码决定按钮禁用文案与引导），
/// 故为 camelCase 字符串字面量而非枚举——新增代码不改变既有值，呈现端遇到不认识的代码
/// 回退显示 <c>Detail</c> 即可。
/// </para>
/// <para>
/// 判定这些代码的唯一实现是 <see cref="Services.Interfaces.ILedgerPostingService.GetReversibilityAsync"/>，
/// 它与冲销漏斗 <c>LedgerPostingEngine.BuildReversalAsync</c> 共用同一段守卫
/// （<c>ReversalGuard</c>），因此"查询说能冲、真冲吃 409"不会发生。
/// </para>
/// </remarks>
public static class ReversalBlockReasons
{
    /// <summary>已被冲销（原凭证已 Reversed，或已挂着冲销凭证）</summary>
    public const string AlreadyReversed = "alreadyReversed";

    /// <summary>草稿，无可冲销（删除草稿即可）</summary>
    public const string NotPosted = "notPosted";

    /// <summary>冲销日期落在已关闭的会计年度内</summary>
    public const string ClosedPeriod = "closedPeriod";

    /// <summary>总账行落在一张<b>已完成</b>的银行对账内（对账不可重开，无受支持的修复途径）</summary>
    public const string Reconciled = "reconciled";

    /// <summary>总账行已与导入的银行流水行匹配（先解除匹配再冲销）</summary>
    public const string StatementMatched = "statementMatched";
}
