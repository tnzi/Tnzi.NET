namespace Tnzi.Finance.Payroll.Services.Internal;

/// <summary>
/// 把发薪批次的两个来源令牌交给核心的「单据投影凭证不得从总账冲销」清单。
/// </summary>
/// <remarks>
/// 没有这一条时，持 <c>finance.journal.update</c> 的操作员对一张 PayRun 凭证调总账冲销端点会成功：
/// 总账被抵消而 <c>PayRun</c> 仍是 Posted/Paid、工资单仍算在应付工资子账里，各凭证自身平衡、
/// 试算平衡恒为 0，只有人工把应付工资控制科目与工资单合计对一遍才看得出来。
/// 配套：<c>PayRunService.VoidAsync</c> 走 <see cref="ILedgerPostingService.ReverseOnBehalfOfDocumentAsync"/>，
/// 否则这份清单会把批次自己的作废一并堵死。
/// </remarks>
public sealed class PayrollDocumentProjectedSourceTypeProvider : IDocumentProjectedSourceTypeProvider
{
    private static readonly string[] Types =
    [
        PayrollPostingHelper.PayRunSourceType,
        PayrollPostingHelper.PayRunPaymentSourceType,
    ];

    /// <inheritdoc />
    public IReadOnlyCollection<string> SourceTypes => Types;
}
