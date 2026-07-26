namespace Tnzi.Finance.Services;

/// <summary>
/// 默认催收策略：按逾期天数阈值分级
/// </summary>
/// <remarks>
/// 阈值可配（<c>FinanceOptions</c> 的 Dunning 组）。这是最容易解释、也最容易被
/// 会计接受的规则；想按客户等级、合同条款或信用评分判定的，换掉
/// <see cref="IDunningPolicy"/> 即可。
/// </remarks>
public class DefaultDunningPolicy : IDunningPolicy
{
    private readonly FinanceOptions _options;

    public DefaultDunningPolicy(IOptionsSnapshot<FinanceOptions> options)
    {
        _options = Check.NotNull(options).Value;
    }

    public DunningLevel Evaluate(int oldestOverdueDays, decimal overdueAmount)
    {
        // 金额为零就没什么可催的——哪怕天数很大（比如一张已付清但当初拖过期的单据）。
        if (overdueAmount <= 0 || oldestOverdueDays <= 0)
            return DunningLevel.None;

        // 小额不惊动人：为了三块钱发最后通知，只会让对方不再认真看这类邮件。
        if (overdueAmount < _options.DunningMinimumAmount)
            return DunningLevel.None;

        if (oldestOverdueDays >= _options.DunningFinalNoticeDays)
            return DunningLevel.FinalNotice;
        if (oldestOverdueDays >= _options.DunningOverdueDays)
            return DunningLevel.Overdue;
        return DunningLevel.Reminder;
    }
}
