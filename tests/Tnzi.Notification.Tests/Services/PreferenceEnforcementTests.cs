using Tnzi.Notification.Metadata;

namespace Tnzi.Notification.Tests.Services;

/// <summary>
/// 用户通知偏好在发送前的判定表（纯函数）。
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ 这些是<b>纯函数</b>测试，只能证明判定表本身对。
/// 「服务真的调了它」由 <c>Integration/PreferenceSendPathTests</c> 走真实 <c>SendAsync</c> 负责 ——
/// 2026-08-08 修退订时头一版只写了纯函数测试，把服务里的调用整段删掉竟然全绿，
/// 等于造出一个考究、正确、无人问津的过滤器。同一个坑不再挖第二遍。
/// </para>
/// </remarks>
public class PreferenceEnforcementTests
{
    private static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Bob = Guid.Parse("22222222-2222-2222-2222-222222222222");

    // ── 要不要问偏好表 ────────────────────────────────────────────────────────

    /// <summary>
    /// 事务性消息不问偏好 —— 判据与退订逐字一致。
    /// </summary>
    /// <remarks>
    /// 少了这条豁免，一个关掉邮件通道的用户会收不到密码重置，也就是再也登不进来。
    /// 这正是「偏好此前不能接」的那个前置条件，2026-08-08 随退订一起落地。
    /// </remarks>
    [Fact]
    public void ATransactionalMessage_NeverConsultsPreferences()
    {
        var candidates = new List<Recipient> { Recipient(Alice) };

        PreferenceRecipientFilter.ShouldConsultPreferences(Message(isTransactional: true), candidates)
            .ShouldBeFalse();
    }

    [Fact]
    public void AMarketingMessageWithAKnownUser_DoesConsultPreferences()
    {
        var candidates = new List<Recipient> { Recipient(Alice) };

        PreferenceRecipientFilter.ShouldConsultPreferences(Message(isTransactional: false), candidates)
            .ShouldBeTrue();
    }

    /// <summary>
    /// 一个带 <c>UserId</c> 的收件人都没有时不问 —— 纯外部地址的群发无从谈偏好。
    /// </summary>
    [Fact]
    public void ABlastToAddressesOnly_DoesNotConsultPreferences()
    {
        var candidates = new List<Recipient> { Recipient(null), Recipient(null) };

        PreferenceRecipientFilter.ShouldConsultPreferences(Message(false), candidates)
            .ShouldBeFalse();
    }

    [Fact]
    public void AnEmptyBatch_DoesNotConsultPreferences()
    {
        PreferenceRecipientFilter.ShouldConsultPreferences(Message(false), []).ShouldBeFalse();
    }

    [Fact]
    public void UserIdsToCheck_DeduplicatesAndSkipsAddressOnlyRecipients()
    {
        var candidates = new List<Recipient> { Recipient(Alice), Recipient(null), Recipient(Alice), Recipient(Bob) };

        PreferenceRecipientFilter.UserIdsToCheck(candidates).ShouldBe([Alice, Bob], ignoreOrder: true);
    }

    // ── 剔除语义 ──────────────────────────────────────────────────────────────

    [Fact]
    public void AUserWhoDisabledTheChannel_IsMarkedCancelled_NotRemoved()
    {
        var alice = Recipient(Alice);
        var bob = Recipient(Bob);
        var candidates = new List<Recipient> { alice, bob };

        var remaining = PreferenceRecipientFilter.Apply(candidates, [Bob]);

        remaining.ShouldBe([bob]);
        alice.Status.ShouldBe(NotificationStatus.Cancelled);
        alice.FailureReason.ShouldBe(PreferenceRecipientFilter.DisabledByPreferenceReason);
    }

    /// <summary>
    /// ★★ 刻意<b>不</b>标 <c>Failed</c>：那会让 <c>ResendToFailedRecipientsAsync</c>
    /// 把他重新捞出来再发一遍，等于开一条绕过偏好的后门（与退订同一条理由）。
    /// </summary>
    [Fact]
    public void ABlockedRecipient_IsNotMarkedFailed()
    {
        var alice = Recipient(Alice);

        PreferenceRecipientFilter.Apply([alice], []);

        alice.Status.ShouldNotBe(NotificationStatus.Failed);
    }

    /// <summary>
    /// ★ <c>UserId</c> 为空的收件人<b>原样放行</b> —— 偏好按人、退订按地址。
    /// </summary>
    /// <remarks>
    /// 把「查不到偏好」当成「已关闭」会让整份导入名单一条都发不出去。
    /// </remarks>
    [Fact]
    public void AnAddressOnlyRecipient_PassesThroughUntouched()
    {
        var anonymous = Recipient(null);

        var remaining = PreferenceRecipientFilter.Apply([anonymous], []);

        remaining.ShouldBe([anonymous]);
        anonymous.Status.ShouldBe(NotificationStatus.Pending);
        anonymous.FailureReason.ShouldBeNull();
    }

    /// <summary>对照：全员启用时原样返回，且没有人被打上任何标记。</summary>
    [Fact]
    public void WhenEveryoneStillEnabled_NothingIsTouched()
    {
        var alice = Recipient(Alice);
        var bob = Recipient(Bob);

        var remaining = PreferenceRecipientFilter.Apply([alice, bob], [Alice, Bob]);

        remaining.Count.ShouldBe(2);
        remaining.ShouldAllBe(r => r.Status == NotificationStatus.Pending);
    }

    // ── 夹具 ──────────────────────────────────────────────────────────────────

    private static Message Message(bool isTransactional) => new()
    {
        Type = NotificationType.Email,
        Category = "marketing",
        IsTransactional = isTransactional,
    };

    private static Recipient Recipient(Guid? userId) => new()
    {
        Address = userId is null ? "someone@example.com" : $"{userId}@example.com",
        UserId = userId,
        Status = NotificationStatus.Pending,
    };
}
