namespace Tnzi.Finance.Recurring.Services.Interfaces;

/// <summary>
/// 排期计算：给定周期与锚点，下一次落在哪天
/// </summary>
/// <remarks>
/// 单独成契约而不是写死在生成器里，因为"下一次是哪天"在真实生意里远不只是加一个月：
/// 有的行业跳过周末与法定假日，有的用 4-4-5 会计周历，有的按合同起租日错开。默认
/// 实现走**公历**（<see cref="CalendarRecurrenceSchedule"/>），消费方注册自己的实现
/// 即可整体替换，模板、生成、补齐、界面全部照旧可用。
///
/// 实现必须是**纯函数且严格递增**：<c>Next(x) &gt; x</c> 恒成立，否则补齐循环不会
/// 终止（生成器另有单次上限兜底，但那是护栏不是设计）。
/// </remarks>
public interface IRecurrenceSchedule
{
    /// <summary>
    /// 算出 <paramref name="after"/> 之后的下一个期次日期（date-only）。
    /// </summary>
    /// <param name="after">从哪天之后开始找（不含当天）</param>
    /// <param name="frequency">周期</param>
    /// <param name="interval">每几个周期一次（≥1）</param>
    /// <param name="anchorDay">锚点：月/季/年取几号（1-31，超出当月天数收到月末）；周取星期几（1=周一…7=周日）；null = 跟随 <paramref name="after"/> 那天</param>
    DateTime Next(DateTime after, RecurrenceFrequency frequency, int interval, int? anchorDay);

    /// <summary>
    /// 从起始日开始，算出第一个期次日期（date-only）。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="Next"/> 的差别只在边界：起始日**当天**就可能是第一期，而 Next
    /// 恒不含入参当天。默认实现按锚点把起始日对齐（起始日已经落在锚点上时就是它本身）。
    /// </remarks>
    DateTime First(DateTime startDate, RecurrenceFrequency frequency, int? anchorDay);
}
