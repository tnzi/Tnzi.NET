namespace Tnzi.Finance.Recurring.Services.Internal;

/// <summary>
/// 交给 <see cref="IRecurrenceSchedule"/> 的锚点：没填锚点的月度 / 季度 / 年度模板以起始日那天为锚点。
/// </summary>
/// <remarks>
/// ★ <see cref="RecurringDocument.AnchorDay"/> 为 null 的语义是「跟随起始日那天」，而排期实现拿到 null 时
/// 只能从上一期推（契约如此：它看不到模板）。于是 1 月 31 日起始、没填锚点的月度模板走成
/// 1/31、2/28、3/28、4/28……此后每一期都落在 28 号，年度模板从 2 月 29 日起始则永远停在 2 月 28 日，
/// 没有任何报错。在调用排期之前把起始日补成锚点，「锚点 29/30/31 在短月收到月末、下一期仍回到锚点」
/// 这条规则对没填锚点的模板同样成立；已经漂到 28 号的存量模板，下一期也会自己回到月末。
/// 周度与日度不受影响：按天数推进没有月末夹取，不会漂。
/// </remarks>
internal static class RecurrenceAnchor
{
    /// <summary>某条模板实际使用的锚点</summary>
    internal static int? Effective(RecurringDocument template)
        => Effective(template.Frequency, template.AnchorDay, template.StartDate);

    /// <summary>按周期、填写的锚点与起始日算出实际使用的锚点</summary>
    internal static int? Effective(RecurrenceFrequency frequency, int? anchorDay, DateTime startDate)
    {
        if (anchorDay is > 0)
            return anchorDay;

        return frequency is RecurrenceFrequency.Monthly or RecurrenceFrequency.Quarterly or RecurrenceFrequency.Yearly
            ? startDate.Day
            : anchorDay;
    }
}
