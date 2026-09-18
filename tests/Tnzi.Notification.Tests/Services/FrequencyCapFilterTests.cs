namespace Tnzi.Notification.Tests.Services;

/// <summary>
/// 每小时上限的判定规则（纯函数层）。接线是否真的发生由
/// <c>Integration/FrequencyCapSendPathTests</c> 覆盖 —— 只测纯函数永远不能作为
/// 「机制已接上」的证据，这个模块已经在退订与渠道开关上各栽过一次。
/// </summary>
public class FrequencyCapFilterTests
{
    private static readonly Guid Alice = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Bob = Guid.Parse("33333333-3333-3333-3333-333333333333");

    // ── 要不要去查 ───────────────────────────────────────────────────────────

    [Fact]
    public void ATransactionalMessage_IsNeverConsulted()
    {
        FrequencyCapFilter.ShouldConsultCaps(
            Message(isTransactional: true), [WithUser(Alice)]).ShouldBeFalse();
    }

    [Fact]
    public void AnEmptyBatch_IsNotConsulted()
    {
        FrequencyCapFilter.ShouldConsultCaps(Message(), []).ShouldBeFalse();
    }

    /// <summary>一个有 UserId 的收件人都没有时无从谈上限（纯外部地址的群发）。</summary>
    [Fact]
    public void ABatchOfAddressOnlyRecipients_IsNotConsulted()
    {
        FrequencyCapFilter.ShouldConsultCaps(Message(), [WithUser(null)]).ShouldBeFalse();
    }

    [Fact]
    public void AMarketingBatchWithAKnownUser_IsConsulted()
    {
        FrequencyCapFilter.ShouldConsultCaps(Message(), [WithUser(Alice)]).ShouldBeTrue();
    }

    // ── 判定 ─────────────────────────────────────────────────────────────────

    [Fact]
    public void AUserWithNoCap_PassesThrough()
    {
        var recipients = new List<Recipient> { WithUser(Alice) };

        var remaining = FrequencyCapFilter.Apply(recipients, Caps(), Sent((Alice, 99)));

        remaining.Count.ShouldBe(1);
    }

    [Fact]
    public void AUserUnderTheirCap_PassesThrough()
    {
        var recipients = new List<Recipient> { WithUser(Alice) };

        var remaining = FrequencyCapFilter.Apply(recipients, Caps((Alice, 3)), Sent((Alice, 2)));

        remaining.Count.ShouldBe(1);
    }

    [Fact]
    public void AUserAtTheirCap_IsCancelledWithAReason()
    {
        var alice = WithUser(Alice);

        var remaining = FrequencyCapFilter.Apply([alice], Caps((Alice, 2)), Sent((Alice, 2)));

        remaining.ShouldBeEmpty();
        alice.Status.ShouldBe(NotificationStatus.Cancelled);
        alice.FailureReason.ShouldBe(FrequencyCapFilter.OverFrequencyCapReason);
    }

    /// <summary>★ 标 Cancelled 不是 Failed —— Failed 会被重发路径捞回来，绕过上限。</summary>
    [Fact]
    public void ACappedRecipient_IsNotMarkedFailed()
    {
        var alice = WithUser(Alice);

        FrequencyCapFilter.Apply([alice], Caps((Alice, 1)), Sent((Alice, 1)));

        alice.Status.ShouldNotBe(NotificationStatus.Failed);
    }

    /// <summary>上限按人算：一个人满了不影响同批的其他人。</summary>
    [Fact]
    public void OneCappedUser_DoesNotBlockAnother()
    {
        var alice = WithUser(Alice);
        var bob = WithUser(Bob);

        var remaining = FrequencyCapFilter.Apply([alice, bob], Caps((Alice, 1), (Bob, 5)), Sent((Alice, 1), (Bob, 1)));

        remaining.ShouldBe([bob]);
    }

    /// <summary>★★ 同一批里同一个人的第二条要算进预算，否则一次群发能把上限整个绕过去。</summary>
    [Fact]
    public void ASecondRecipientForTheSameUserInOneBatch_ConsumesTheBudget()
    {
        var first = WithUser(Alice);
        var second = WithUser(Alice);

        var remaining = FrequencyCapFilter.Apply([first, second], Caps((Alice, 1)), Sent());

        remaining.ShouldBe([first]);
        second.Status.ShouldBe(NotificationStatus.Cancelled);
    }

    /// <summary>没有 UserId 的收件人不受上限影响，即使同批有人被拦。</summary>
    [Fact]
    public void AnAddressOnlyRecipient_IsUntouched()
    {
        var anonymous = WithUser(null);
        var alice = WithUser(Alice);

        var remaining = FrequencyCapFilter.Apply([anonymous, alice], Caps((Alice, 1)), Sent((Alice, 1)));

        remaining.ShouldBe([anonymous]);
        anonymous.Status.ShouldBe(NotificationStatus.Pending);
    }

    // ── 夹具 ─────────────────────────────────────────────────────────────────

    private static Message Message(bool isTransactional = false) => new()
    {
        Subject = "Spring sale",
        Content = "Body",
        Type = NotificationType.Email,
        Category = "Marketing",
        IsTransactional = isTransactional,
    };

    private static Recipient WithUser(Guid? userId) => new()
    {
        Address = "someone@example.com",
        UserId = userId,
        Status = NotificationStatus.Pending,
    };

    private static Dictionary<Guid, int> Caps(params (Guid User, int Cap)[] entries)
        => entries.ToDictionary(e => e.User, e => e.Cap);

    private static Dictionary<Guid, int> Sent(params (Guid User, int Count)[] entries)
        => entries.ToDictionary(e => e.User, e => e.Count);
}
