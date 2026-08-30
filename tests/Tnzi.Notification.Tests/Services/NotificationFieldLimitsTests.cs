using Tnzi.Notification.Services.Internal;

namespace Tnzi.Notification.Tests.Services;

/// <summary>
/// 投递结果落库前的长度收敛（纯函数层）。
/// </summary>
/// <remarks>
/// 接线是否真的发生由 <c>Integration/DeliveryResultPersistenceTests</c> 覆盖 ——
/// 只测纯函数永远不能作为「机制已接上」的证据。
/// </remarks>
public class NotificationFieldLimitsTests
{
    [Fact]
    public void AShortReason_IsReturnedUnchanged()
    {
        NotificationFieldLimits.TruncateFailureReason("smtp: 550 mailbox unavailable")
            .ShouldBe("smtp: 550 mailbox unavailable");
    }

    [Fact]
    public void Null_StaysNull()
    {
        NotificationFieldLimits.TruncateFailureReason(null).ShouldBeNull();
    }

    /// <summary>刚好等于列宽的原文不该被动 —— 否则每条都要付一次省略号的代价。</summary>
    [Fact]
    public void AReasonExactlyAtTheLimit_IsNotTouched()
    {
        var exact = new string('x', NotificationFieldLimits.FailureReasonMaxLength);

        NotificationFieldLimits.TruncateFailureReason(exact).ShouldBe(exact);
    }

    [Fact]
    public void AnOversizeReason_IsCutToTheColumnWidth()
    {
        var huge = new string('x', 8000);

        var bounded = NotificationFieldLimits.TruncateFailureReason(huge);

        bounded!.Length.ShouldBe(NotificationFieldLimits.FailureReasonMaxLength);
    }

    /// <summary>
    /// 截断处要留下痕迹：一段刚好 1000 字符的网关响应，与一段被截到 1000 的，
    /// 在运维眼里必须能分开。
    /// </summary>
    [Fact]
    public void AnOversizeReason_IsMarkedAsCut()
    {
        NotificationFieldLimits.TruncateFailureReason(new string('x', 8000))!
            .ShouldEndWith("…");
    }

    /// <summary>
    /// ★ 切点落在代理对中间时要退一格。留下孤立的高代理项就是一个非法 UTF-16 串，
    /// PostgreSQL 编码成 UTF-8 时会拒绝整条插入 —— 一个防写库失败的函数自己造出写库失败。
    /// </summary>
    [Fact]
    public void ASurrogatePairAtTheCutPoint_IsNotSplit()
    {
        // 省略号占 1 个码元，所以正文切在 999；把一个 emoji 摆在 998-999 这两格上。
        var text = new string('a', NotificationFieldLimits.FailureReasonMaxLength - 2)
                   + "\U0001F600"
                   + new string('b', 50);

        var bounded = NotificationFieldLimits.TruncateFailureReason(text)!;

        bounded.Length.ShouldBe(NotificationFieldLimits.FailureReasonMaxLength - 1);
        char.IsSurrogate(bounded[^2]).ShouldBeFalse();
        bounded.ToCharArray().ShouldNotContain(c => char.IsHighSurrogate(c));
    }

    [Fact]
    public void AnExternalMessageIdWithinTheColumn_IsKept()
    {
        var accepted = NotificationFieldLimits.AcceptExternalMessageId("<abc@mailer>", out var dropped);

        accepted.ShouldBe("<abc@mailer>");
        dropped.ShouldBeNull();
    }

    /// <summary>
    /// ★ 外部消息号<b>丢弃而不截断</b>：传真回执按它等值对号，截出来的值看起来合法却
    /// 永远对不上 —— 那比空着糟。空着至少让「对不上号」是可见的。
    /// </summary>
    [Fact]
    public void AnOversizeExternalMessageId_IsDroppedNotTruncated()
    {
        var huge = new string('m', NotificationFieldLimits.ExternalMessageIdMaxLength + 1);

        var accepted = NotificationFieldLimits.AcceptExternalMessageId(huge, out var dropped);

        accepted.ShouldBeNull();
        dropped.ShouldBe(huge);
    }
}
