namespace Tnzi.Finance.Metadata;

/// <summary>
/// 对账单形态
/// </summary>
/// <remarks>
/// 北美两种通行做法，都得支持——它们回答的是不同的问题：
/// <list type="bullet">
/// <item><b>Open Item</b>：只列还没付清的单据。回答"你现在欠我哪几张"，催收用。</item>
/// <item><b>Activity</b>（又叫 Balance Forward）：期初余额 + 本期全部往来 + 期末余额。
///   回答"这段时间我们之间发生了什么"，月结对账用。</item>
/// </list>
/// </remarks>
public enum StatementStyle
{
    /// <summary>只列未清单据</summary>
    OpenItem = 1,

    /// <summary>期初余额 + 本期活动 + 期末余额</summary>
    Activity = 2
}

/// <summary>
/// 催收强度
/// </summary>
/// <remarks>
/// 强度是**建议**不是动作：框架不替消费应用发邮件、不停单、不加滞纳金——那些是
/// 各自的商业决定。它只回答"这个往来方逾期到什么程度了"，动作由消费应用接。
/// </remarks>
public enum DunningLevel
{
    /// <summary>无需跟进</summary>
    None = 0,

    /// <summary>友好提醒（刚过期）</summary>
    Reminder = 1,

    /// <summary>已逾期</summary>
    Overdue = 2,

    /// <summary>最后通知</summary>
    FinalNotice = 3
}
