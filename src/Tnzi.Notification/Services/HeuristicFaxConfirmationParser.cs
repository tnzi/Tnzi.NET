namespace Tnzi.Notification.Services;

/// <summary>
/// 默认的回执判读器：按信头对号，按措辞判结论。
/// </summary>
/// <remarks>
/// <para>
/// ★ <b>它一定会遇到不认识的网关</b>，这不是缺陷而是前提 —— email-to-fax 的回执格式没有标准。
/// 所以它的每一条规则都往"读不懂"那边倒：拿不准就返回 <c>null</c> 或
/// <see cref="FaxDeliveryOutcome.Unknown"/>，让上层什么都不做。要准确，就照自家网关的格式
/// 注册一个 <see cref="IFaxConfirmationParser"/> 覆盖掉它。
/// </para>
/// <para>
/// ★★ <b>成功词优先于失败词</b>：一封写着 "Your fax was sent successfully - 0 failures" 的回执
/// 两种词都有。若按"见到失败词就算失败"，一份送达的传真会被标成失败，然后有人去重发，
/// 对方收到两份。所以两种词同时出现时判 <see cref="FaxDeliveryOutcome.Unknown"/> —— 什么都不做，
/// 保持它原本的 <c>Sent</c>。
/// </para>
/// </remarks>
public class HeuristicFaxConfirmationParser : IFaxConfirmationParser
{
    /// <summary>
    /// 判"没送到"的措辞。
    /// </summary>
    /// <remarks>
    /// 都是小写，比对前把待查文本整体转小写。收的是网关自动生成的英文回执 ——
    /// 中文网关请自行注册解析器，这里不去猜。
    /// </remarks>
    private static readonly string[] FailureMarkers =
    [
        "failed", "failure", "unsuccessful", "undeliverable", "not delivered",
        "could not be delivered", "unable to send", "no answer", "busy",
        "invalid number", "wrong number", "transmission error"
    ];

    /// <summary>判"送到了"的措辞。</summary>
    private static readonly string[] SuccessMarkers =
    [
        "successful", "successfully", "delivered", "transmission ok",
        "fax sent", "sent successfully", "confirmation of delivery"
    ];

    /// <summary>正文里往回看多少字符。回执正文可能挂着长长的免责声明或原信引用。</summary>
    private const int BodyScanLimit = 4000;

    /// <summary>号码：7-15 位数字，中间允许人写的分隔符。</summary>
    /// <remarks>
    /// ★ <b>已知边界</b>：空白算分隔符（<c>905 555 1234</c> 是一个号码），所以主题里两个相邻的
    /// 独立数字串会被粘成一个，例如 <c>Fax 12345 9055551234 failed</c> 读出的是
    /// <c>123459055551234</c>。它几乎总是归一化失败或对不上任何收件人，结果是"这封回执没对上号" ——
    /// 无害。真正要错得有后果，得让粘出来的数字恰好等于窗口内另一份传真的号码。
    /// 之所以不去收紧：禁掉空白会让绝大多数正常写法读不出来，为一个概率极低的误配
    /// 换掉主要用途，不划算。号码这条路本来就有窗口 + 只认 <c>Sent</c> + 精确等值三道兜底。
    /// </remarks>
    private static readonly Regex NumberPattern = new(
        @"\+?\d[\d\s\-\(\)\./]{5,20}\d", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <inheritdoc />
    public FaxConfirmation? Parse(FaxConfirmationMessage message)
    {
        Check.NotNull(message);

        var carrierMessageId = ResolveCarrierMessageId(message);
        var faxNumber = ResolveFaxNumber(message);

        // 两个线索一个都没有 = 无从对号。就算读出了结论也不知道该记在谁头上。
        if (carrierMessageId == null && faxNumber == null)
            return null;

        var text = BuildScanText(message);
        var outcome = ReadOutcome(text);

        return new FaxConfirmation(
            outcome,
            carrierMessageId,
            faxNumber,
            outcome == FaxDeliveryOutcome.Failed ? Summarise(message.Subject) : null,
            message.ReceivedAt);
    }

    /// <summary>
    /// 精确对号：<c>In-Reply-To</c>，其次 <c>References</c> 的最后一个（最近的父邮件）。
    /// </summary>
    private static string? ResolveCarrierMessageId(FaxConfirmationMessage message)
    {
        var inReplyTo = NormalizeMessageId(message.InReplyTo);
        if (inReplyTo != null)
            return inReplyTo;

        for (var i = message.References.Count - 1; i >= 0; i--)
        {
            var reference = NormalizeMessageId(message.References[i]);
            if (reference != null)
                return reference;
        }

        return null;
    }

    /// <summary>
    /// 去掉 Message-ID 两侧的尖括号。信头里带尖括号，而
    /// <see cref="Entities.Recipient.ExternalMessageId"/> 存的是 MimeKit 给的裸值 ——
    /// 不统一就永远匹配不上，且症状是"精确对号从来不生效，一直在走号码退路"。
    /// </summary>
    private static string? NormalizeMessageId(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var trimmed = raw.Trim().Trim('<', '>').Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    /// <summary>
    /// 退路对号：从主题里找一个能归一化的传真号。
    /// </summary>
    /// <remarks>
    /// ★ 只找**主题**不找正文：正文里常常还有发件人自己的传真号、客服电话、页脚里的公司总机 ——
    /// 在那里面挑一个"看起来像传真号"的，挑错的概率比挑对高。主题里出现的号码几乎总是这一份的收件号。
    /// </remarks>
    private static string? ResolveFaxNumber(FaxConfirmationMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.Subject))
            return null;

        foreach (Match match in NumberPattern.Matches(message.Subject))
        {
            if (FaxNumber.TryNormalize(match.Value, out var normalized, out _))
                return normalized;
        }

        return null;
    }

    private static string BuildScanText(FaxConfirmationMessage message)
    {
        var body = message.Body ?? string.Empty;
        if (body.Length > BodyScanLimit)
            body = body[..BodyScanLimit];

        return $"{message.Subject} {body}".ToLowerInvariant();
    }

    /// <summary>
    /// 读结论。两种措辞都出现时判 <see cref="FaxDeliveryOutcome.Unknown"/> —— 理由见类注释。
    /// </summary>
    private static FaxDeliveryOutcome ReadOutcome(string text)
    {
        var failed = FailureMarkers.Any(marker => text.Contains(marker, StringComparison.Ordinal));
        var delivered = SuccessMarkers.Any(marker => text.Contains(marker, StringComparison.Ordinal));

        if (failed && delivered)
            return FaxDeliveryOutcome.Unknown;

        if (failed)
            return FaxDeliveryOutcome.Failed;

        return delivered ? FaxDeliveryOutcome.Delivered : FaxDeliveryOutcome.Unknown;
    }

    /// <summary>把主题原样留作失败原因 —— 网关写的那句话比框架转述的准。</summary>
    private static string? Summarise(string? subject)
    {
        if (string.IsNullOrWhiteSpace(subject))
            return null;

        var trimmed = subject.Trim();
        return trimmed.Length <= 300 ? trimmed : trimmed[..300];
    }
}
