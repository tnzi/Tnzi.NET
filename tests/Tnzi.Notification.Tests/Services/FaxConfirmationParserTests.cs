namespace Tnzi.Notification.Tests.Services;

/// <summary>
/// <see cref="HeuristicFaxConfirmationParser"/>：怎么对号、怎么读结论、什么时候拒绝表态。
/// </summary>
/// <remarks>
/// ★ 这组的重点不是"读得多准"，而是**读不准时会不会闭嘴**。回执格式没有标准，判读器一定会
/// 遇到不认识的网关；一次误判的代价是把送达的传真标成失败（有人去重发，对方收到两份），
/// 或者更糟 —— 但后者被架构挡住了：只有 Failed 会改动数据。
/// </remarks>
public class FaxConfirmationParserTests
{
    private readonly IFaxConfirmationParser _parser = new HeuristicFaxConfirmationParser();

    private static FaxConfirmationMessage Message(
        string? subject = null,
        string? body = null,
        string? inReplyTo = null,
        IReadOnlyList<string>? references = null)
        => new()
        {
            Subject = subject,
            Body = body,
            InReplyTo = inReplyTo,
            References = references ?? [],
            ReceivedAt = new DateTimeOffset(2026, 8, 20, 10, 0, 0, TimeSpan.Zero)
        };

    #region 对号

    /// <summary>
    /// ★ 信头里的 Message-ID 带尖括号，而 <c>Recipient.ExternalMessageId</c> 存的是裸值。
    /// 不统一就永远匹配不上，症状是"精确对号从来不生效，一直在走号码退路"。
    /// </summary>
    [Fact]
    public void Parse_StripsTheAngleBracketsFromTheMessageId()
    {
        var result = _parser.Parse(Message(
            subject: "Fax delivery failed",
            inReplyTo: "<abc123@mail.example.com>"));

        result!.CarrierMessageId.ShouldBe("abc123@mail.example.com");
    }

    /// <summary>没有 In-Reply-To 时退到 References 的最后一个（最近的父邮件）。</summary>
    [Fact]
    public void Parse_FallsBackToTheLastReference()
    {
        var result = _parser.Parse(Message(
            subject: "Fax delivery failed",
            references: ["<root@example.com>", "<parent@mail.example.com>"]));

        result!.CarrierMessageId.ShouldBe("parent@mail.example.com");
    }

    [Fact]
    public void Parse_ReadsTheFaxNumberFromTheSubject()
    {
        var result = _parser.Parse(Message(subject: "Fax to +1 (905) 555-1234 failed"));

        result!.FaxNumber.ShouldBe("9055551234");
    }

    /// <summary>
    /// ★ 号码只从**主题**里找。正文里常常还有发件人自己的传真号、客服电话、页脚的公司总机 ——
    /// 在那里面挑一个"看起来像传真号"的，挑错的概率比挑对高。
    /// </summary>
    [Fact]
    public void Parse_DoesNotMineTheBodyForNumbers()
    {
        var result = _parser.Parse(Message(
            subject: "Delivery failure notice",
            body: "Our support line is 905-555-0000. Please call if this repeats."));

        result.ShouldBeNull("正文里的号码是别人的，认它比认不出来更糟");
    }

    /// <summary>两条线索一个都没有 = 无从对号，读出结论也不知道记在谁头上。</summary>
    [Fact]
    public void Parse_WithNeitherAMessageIdNorANumber_ReturnsNull()
    {
        _parser.Parse(Message(subject: "Delivery failed", body: "no identifying detail")).ShouldBeNull();
    }

    #endregion

    #region 结论

    [Theory]
    [InlineData("Fax to 9055551234 FAILED")]
    [InlineData("Transmission error sending to 9055551234")]
    [InlineData("Fax to 9055551234: no answer")]
    [InlineData("Fax to 9055551234 was undeliverable")]
    [InlineData("Fax to 9055551234 - line busy")]
    public void Parse_ReadsAFailure(string subject)
    {
        _parser.Parse(Message(subject))!.Outcome.ShouldBe(FaxDeliveryOutcome.Failed);
    }

    [Theory]
    [InlineData("Fax to 9055551234 delivered")]
    [InlineData("Fax to 9055551234 sent successfully")]
    [InlineData("Confirmation of delivery - 9055551234")]
    public void Parse_ReadsADelivery(string subject)
    {
        _parser.Parse(Message(subject))!.Outcome.ShouldBe(FaxDeliveryOutcome.Delivered);
    }

    /// <summary>
    /// ★★ 全组最要紧的一条：两种措辞同时出现时**拒绝表态**。
    /// </summary>
    /// <remarks>
    /// "Your fax was sent successfully - 0 failures" 里两种词都有。若按"见到失败词就算失败"，
    /// 一份送达的传真会被标成失败，然后有人去重发，对方收到两份。判 Unknown = 什么都不做，
    /// 那份传真保持它原本的 Sent。
    /// </remarks>
    [Fact]
    public void Parse_WithBothWordings_RefusesToDecide()
    {
        var result = _parser.Parse(Message(
            subject: "Fax to 9055551234 sent successfully",
            body: "Pages: 3. Failed pages: 0."));

        result!.Outcome.ShouldBe(FaxDeliveryOutcome.Unknown);
    }

    /// <summary>认得出是回执、读不出结论 —— 同样什么都不做。</summary>
    [Fact]
    public void Parse_WithNoRecognisableWording_ReportsUnknown()
    {
        var result = _parser.Parse(Message(subject: "Job 8891 for 9055551234", body: "Pages: 3"));

        result!.Outcome.ShouldBe(FaxDeliveryOutcome.Unknown);
    }

    /// <summary>失败原因保留网关自己的措辞 —— 它比框架转述的准。</summary>
    [Fact]
    public void Parse_KeepsTheGatewaysOwnWordingAsTheReason()
    {
        var result = _parser.Parse(Message(subject: "Fax to 9055551234 failed: invalid number"));

        result!.Reason.ShouldBe("Fax to 9055551234 failed: invalid number");
    }

    /// <summary>只有失败才带原因：另外两种结论不会改动任何数据，原因也就无处可写。</summary>
    [Fact]
    public void Parse_DoesNotCarryAReasonForADelivery()
    {
        _parser.Parse(Message(subject: "Fax to 9055551234 delivered"))!.Reason.ShouldBeNull();
    }

    #endregion
}
