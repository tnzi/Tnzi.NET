namespace Tnzi.Notification.Dtos;

/// <summary>
/// 一封待判读的邮件，交给 <see cref="Services.IFaxConfirmationParser"/> 看它是不是传真回执。
/// </summary>
/// <remarks>
/// ★ <b>刻意不是 MimeKit 的 <c>MimeMessage</c></b>：回执不一定从 IMAP 来。有的部署用网关的
/// webhook，有的把回执转发进一个队列，还有的干脆人工补录。用一个只有几个字段的普通类，
/// 这几条来源写起来都一样，解析器也就不必依赖任何邮件库。
/// </remarks>
public sealed class FaxConfirmationMessage
{
    /// <summary>邮件主题。多数网关把结论写在这里。</summary>
    public string? Subject { get; init; }

    /// <summary>邮件正文（纯文本）。</summary>
    public string? Body { get; init; }

    /// <summary>发件地址。</summary>
    public string? From { get; init; }

    /// <summary>
    /// <c>In-Reply-To</c> 信头：回执若是对原邮件的回复，这里就是那封承载邮件的 Message-ID。
    /// </summary>
    public string? InReplyTo { get; init; }

    /// <summary><c>References</c> 信头里的全部 Message-ID，最后一个通常是直接父邮件。</summary>
    public IReadOnlyList<string> References { get; init; } = [];

    /// <summary>收到时间。</summary>
    public DateTimeOffset ReceivedAt { get; init; }
}

/// <summary>
/// 从一封回执里读出来的结论，以及把它对回某一份传真所需的线索。
/// </summary>
/// <param name="Outcome">结论。只有 <see cref="FaxDeliveryOutcome.Failed"/> 会改动数据。</param>
/// <param name="CarrierMessageId">
/// 承载那份传真的邮件的 Message-ID（来自 <c>In-Reply-To</c> / <c>References</c>）。
/// 这是唯一精确的对号方式，与 <see cref="Entities.Recipient.ExternalMessageId"/> 对上。
/// </param>
/// <param name="FaxNumber">
/// 回执里提到的传真号码（已归一化）。<c>CarrierMessageId</c> 拿不到时的退路 ——
/// 不精确，所以只在一个时间窗口内匹配。
/// </param>
/// <param name="Reason">失败原因原文，写进收件人的 <c>FailureReason</c>。</param>
/// <param name="ReceivedAt">回执收到的时间。</param>
public sealed record FaxConfirmation(
    FaxDeliveryOutcome Outcome,
    string? CarrierMessageId,
    string? FaxNumber,
    string? Reason,
    DateTimeOffset ReceivedAt);
