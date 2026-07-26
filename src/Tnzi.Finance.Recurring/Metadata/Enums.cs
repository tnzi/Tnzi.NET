namespace Tnzi.Finance.Recurring.Metadata;

/// <summary>
/// 周期
/// </summary>
public enum RecurrenceFrequency
{
    Daily = 1,
    Weekly = 2,
    Monthly = 3,
    Quarterly = 4,
    Yearly = 5
}

/// <summary>
/// 生成什么单据
/// </summary>
/// <remarks>
/// 只覆盖有明确周期性用途的三种：订阅/租金开发票、固定供应商账单、按月摊的费用。
/// 收付款单不在列——钱什么时候到不由日历决定。
/// </remarks>
public enum RecurringDocKind
{
    Invoice = 1,
    Bill = 2,
    Expense = 3
}

/// <summary>
/// 模板状态
/// </summary>
public enum RecurringStatus
{
    /// <summary>运行中</summary>
    Active = 1,

    /// <summary>已暂停（保留排期，不生成）</summary>
    Paused = 2,

    /// <summary>已结束（到达结束日或手工终止）</summary>
    Ended = 3
}

/// <summary>
/// 作业停机之后怎么补
/// </summary>
/// <remarks>
/// ★**必须由消费方决定**，框架不替他们选：作业停了一周，是应该补出七张日租发票
/// （GenerateAll），还是只出最近一张（LatestOnly），还是干脆跳过让人自己看着办
/// （Skip）——三种答案在不同生意里都是对的，而猜错的代价是凭空多出或少掉真金白银
/// 的单据。
/// </remarks>
public enum RecurringCatchUpPolicy
{
    /// <summary>把错过的每一期都补出来</summary>
    GenerateAll = 1,

    /// <summary>只补最近一期，其余跳过</summary>
    LatestOnly = 2,

    /// <summary>一期都不补，直接把排期推到下一次</summary>
    Skip = 3
}

/// <summary>
/// 一次生成的结果
/// </summary>
public enum RecurringRunStatus
{
    /// <summary>已生成</summary>
    Generated = 1,

    /// <summary>按补齐策略跳过（这一期不生成，但记下来）</summary>
    Skipped = 2,

    /// <summary>失败（不占幂等键，下次扫描重试）</summary>
    Failed = 3
}
