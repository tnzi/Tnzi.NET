namespace Tnzi.Finance.Services;

/// <summary>
/// 催收强度判定（<c>TryAddScoped</c> 默认实现，消费应用可整体替换）
/// </summary>
/// <remarks>
/// **只回答"逾期到什么程度"，不做任何动作**：发不发邮件、停不停单、加不加滞纳金
/// 都是各家的商业决定，框架替消费应用决定这些只会碍事。
///
/// 默认实现按天数阈值分级（可配置）。想按客户等级、按合同条款、按信用评分判定的，
/// 换掉本接口即可——对账单与催收工作台照旧可用。
/// </remarks>
public interface IDunningPolicy
{
    /// <summary>
    /// 按逾期天数与金额给出强度建议。
    /// </summary>
    /// <param name="oldestOverdueDays">最久那笔的逾期天数（无逾期传 0）</param>
    /// <param name="overdueAmount">逾期金额</param>
    DunningLevel Evaluate(int oldestOverdueDays, decimal overdueAmount);
}
