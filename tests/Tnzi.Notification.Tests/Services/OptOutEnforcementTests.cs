using Tnzi.Notification.Metadata;

namespace Tnzi.Notification.Tests.Services;

/// <summary>
/// 发送前的退订判定。
/// </summary>
/// <remarks>
/// <para>
/// <b>被保护的缺陷</b>：退订表、一键退订令牌、<c>INotificationOptOutService.FilterAllowedAsync</c>
/// （其文档白纸黑字写着"群发前应当调这个"）此前<b>全都建好了，但没有任何调用方</b>。
/// 收件人点退订 → 拿到 200 → 库里多一行 → 照收不误。
/// </para>
/// <para>
/// ★ 这比没有退订功能更糟：它是一个会被当真的承诺，而且是合规场景里会被当真的那种。
/// 端点在、表在、文档在，唯独发送路径不认识它们 —— 读代码时每一处单看都对。
/// </para>
/// </remarks>
public class OptOutEnforcementTests
{
    private static Message Message(bool isTransactional = false)
        => new()
        {
            Id = Guid.NewGuid(),
            Type = NotificationType.Email,
            Category = "Marketing",
            IsTransactional = isTransactional
        };

    private static List<Recipient> Recipients(params string[] addresses)
        => addresses
            .Select(a => new Recipient { Address = a, Status = NotificationStatus.Pending })
            .ToList();

    /// <summary>
    /// 已退订的收件人被择出去，其余照常。
    /// </summary>
    [Fact]
    public void Apply_RemovesTheAddressesThatOptedOut()
    {
        var recipients = Recipients("keep@example.com", "gone@example.com", "also-keep@example.com");

        var remaining = OptOutRecipientFilter.Apply(
            recipients, ["keep@example.com", "also-keep@example.com"]);

        remaining.Select(r => r.Address)
            .ShouldBe(["keep@example.com", "also-keep@example.com"]);
        remaining.ShouldAllBe(r => r.Status == NotificationStatus.Pending);
    }

    /// <summary>
    /// 被择出去的人标 Cancelled 并留下原因，而不是从记录里消失。
    /// </summary>
    /// <remarks>
    /// 合规场景要的是"证明没有发过"。把人从列表里删掉恰恰证明不了这件事。
    /// </remarks>
    [Fact]
    public void Apply_MarksTheExcludedRecipientsCancelledWithAReason()
    {
        var recipients = Recipients("gone@example.com");

        OptOutRecipientFilter.Apply(recipients, []);

        recipients[0].Status.ShouldBe(NotificationStatus.Cancelled);
        recipients[0].FailureReason.ShouldBe(OptOutRecipientFilter.OptedOutReason);
    }

    /// <summary>
    /// ★ 绝不能标成 Failed。
    /// </summary>
    /// <remarks>
    /// <c>ResendToFailedRecipientsAsync</c> 会把 <c>Failed</c> 的收件人重新捞出来再发一遍 ——
    /// 用 Failed 表示"因退订而未发"，等于亲手开了一条绕过退订的后门。
    /// </remarks>
    [Fact]
    public void Apply_NeverMarksAnOptedOutRecipientFailed()
    {
        var recipients = Recipients("gone@example.com");

        OptOutRecipientFilter.Apply(recipients, []);

        recipients[0].Status.ShouldNotBe(NotificationStatus.Failed,
            "Failed 会被重发路径捞回来，等于给退订开后门");
    }

    /// <summary>
    /// 地址比较不区分大小写。
    /// </summary>
    /// <remarks>
    /// <c>FilterAllowedAsync</c> 会归一化地址后再返回，拿原始字符串做等值比较会把
    /// 大小写不同的合法收件人误判成已退订 —— 一个只在真实数据上才会出现的静默错发/漏发。
    /// </remarks>
    [Fact]
    public void Apply_MatchesAddressesCaseInsensitively()
    {
        var recipients = Recipients("Mixed.Case@Example.COM");

        var remaining = OptOutRecipientFilter.Apply(recipients, ["mixed.case@example.com"]);

        remaining.Count.ShouldBe(1);
        remaining[0].Status.ShouldBe(NotificationStatus.Pending);
    }

    /// <summary>
    /// 没有人退订时原样返回，不做任何标记。
    /// </summary>
    [Fact]
    public void Apply_WithNobodyOptedOut_LeavesEveryRecipientAlone()
    {
        var recipients = Recipients("a@example.com", "b@example.com");

        var remaining = OptOutRecipientFilter.Apply(
            recipients, ["a@example.com", "b@example.com"]);

        remaining.ShouldBe(recipients);
        remaining.ShouldAllBe(r => r.FailureReason == null);
    }

    /// <summary>
    /// 商业消息要问退订名单。
    /// </summary>
    [Fact]
    public void ShouldConsultOptOutList_ForACommercialMessage_IsTrue()
    {
        OptOutRecipientFilter.ShouldConsultOptOutList(Message(), candidateCount: 3).ShouldBeTrue();
    }

    /// <summary>
    /// ★ 事务性消息不问 —— 退订按钮管的是营销邮件。
    /// </summary>
    /// <remarks>
    /// 少了这条豁免，一个点过退订的人会收不到密码重置链接和二次验证码，也就是<b>再也登不进来</b>。
    /// 框架内的密码重置 / 2FA / 注册确认 / 账单与订阅通知都带着这个标志。
    /// </remarks>
    [Fact]
    public void ShouldConsultOptOutList_ForATransactionalMessage_IsFalse()
    {
        OptOutRecipientFilter
            .ShouldConsultOptOutList(Message(isTransactional: true), candidateCount: 3)
            .ShouldBeFalse("退订不该让人再也收不到验证码");
    }

    /// <summary>
    /// 空批次不问，省一次无谓往返。
    /// </summary>
    [Fact]
    public void ShouldConsultOptOutList_WithNoCandidates_IsFalse()
    {
        OptOutRecipientFilter.ShouldConsultOptOutList(Message(), candidateCount: 0).ShouldBeFalse();
    }

    /// <summary>
    /// 默认值是"商业消息"：拿不准就按受退订约束处理。
    /// </summary>
    /// <remarks>
    /// 默认方向必须是"宁可少发一条"。反过来（默认豁免）会让每一个忘记设标志的调用方
    /// 都悄悄绕过退订，而这种疏漏没有任何症状 —— 正是本次缺陷的形态。
    /// </remarks>
    [Fact]
    public void ANewRequest_IsCommercialByDefault()
    {
        new CreateNotificationRequest().IsTransactional.ShouldBeFalse();
        new Message().IsTransactional.ShouldBeFalse();
    }
}
