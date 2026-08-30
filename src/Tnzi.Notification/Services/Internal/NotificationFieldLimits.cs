namespace Tnzi.Notification.Services.Internal;

/// <summary>
/// 投递结果落库前要收敛到的列宽，以及越界时该怎么办。
/// </summary>
/// <remarks>
/// <para>
/// ★ <b>为什么收在这一层而不是收在网关适配器里</b>：<see cref="IEmailSender"/> /
/// <see cref="ISmsSender"/> / <see cref="IPushSender"/> / <see cref="IFaxSender"/> 都是
/// <b>可替换的公开契约</b>，消费应用注册自己的实现是受支持的用法。把长度纪律写进某一个
/// 内置发送器，换一个实现就绕过去了 —— 而 <c>NotificationService</c> 把 <see cref="SendResult"/>
/// 抄进实体的那几行，才是所有实现共同必经的<b>持久化边界</b>。
/// </para>
/// <para>
/// ★★ <b>为什么这件事比「一条记录的原因字段被截了」严重得多</b>：一次群发的全部收件人
/// 状态由<b>一次</b> <c>SaveChangesAsync</c> 落库。任何一条越界都让整次保存抛异常并回滚，
/// 于是<b>已经真的发出去</b>的那些收件人留在 <c>Pending</c>；而
/// <c>NotificationDispatchBackgroundService</c> 的续发注释（「SendAsync 只发 Pending/Failed
/// 的收件人，所以续发不会重复投递」）正建立在这些状态已经落库的前提上。前提被丢掉，
/// 续发就会把已投递的那批<b>再发一遍</b>，直到网关不再返回那个超长响应为止。
/// 也就是说，越界的直接后果不是「少了一行原因」，是<b>重复投递</b>。
/// </para>
/// <para>
/// ★ <b>失败原因截断，外部消息号丢弃</b>，判据与 <c>Tnzi.Finance.Banking</c> 的
/// <c>ReceiptFieldLimits</c> 一致：「错的值比空着更糟还是更好」。失败原因是<b>给人读</b>的，
/// 留开头比整条丢掉有用；而外部消息号<b>会被下游拿去精确比对</b>（传真回执按
/// <c>ExternalMessageId</c> 等值对号），截出来的值看起来合法却永远对不上号 —— 那比空着糟。
/// </para>
/// <para>
/// 常量由 <c>RecipientConfiguration</c> 与 <c>MessageConfiguration</c> 直接引用，
/// 三处不可能漂移。
/// </para>
/// </remarks>
internal static class NotificationFieldLimits
{
    /// <summary><see cref="Recipient.FailureReason"/> 与 <see cref="Message.FailureReason"/> 的列宽。</summary>
    internal const int FailureReasonMaxLength = 1000;

    /// <summary><see cref="Recipient.ExternalMessageId"/> 的列宽。</summary>
    internal const int ExternalMessageIdMaxLength = 200;

    /// <summary>
    /// 把失败原因收敛到列装得下的长度。<see langword="null"/> 原样返回。
    /// </summary>
    /// <remarks>
    /// 截断处补省略号是刻意的：一段刚好卡在 1000 字符的网关 HTML，与一段被截到 1000 的，
    /// 在运维眼里必须能分开 —— 否则他会以为自己看到的是全文。
    /// </remarks>
    internal static string? TruncateFailureReason(string? reason)
    {
        if (reason == null || reason.Length <= FailureReasonMaxLength)
            return reason;

        const string ellipsis = "…";
        return Cut(reason, FailureReasonMaxLength - ellipsis.Length) + ellipsis;
    }

    /// <summary>
    /// 收敛网关给回的外部消息号：超长即丢弃并回报，<b>不截断</b>。
    /// </summary>
    /// <param name="externalMessageId">发送器返回的原值。</param>
    /// <param name="dropped">
    /// 被丢弃时为原值（供调用方记一条日志），否则为 <see langword="null"/>。
    /// </param>
    internal static string? AcceptExternalMessageId(string? externalMessageId, out string? dropped)
    {
        if (externalMessageId == null || externalMessageId.Length <= ExternalMessageIdMaxLength)
        {
            dropped = null;
            return externalMessageId;
        }

        dropped = externalMessageId;
        return null;
    }

    /// <summary>
    /// 按<b>列宽</b>（UTF-16 码元数）截断，且不留下半个代理对。
    /// </summary>
    /// <remarks>
    /// ★ 不能用框架的 <c>TruncateByTextElements</c>：那个按<b>字素簇</b>计数，而列宽约束
    /// 是码元数，一个 emoji 就能让「截到 1000 个字素」仍然超过 <c>varchar(1000)</c>。
    /// <para>
    /// ★ 但裸 <c>text[..max]</c> 可能正好切在代理对中间，留下一个孤立的高代理项 ——
    /// 那是一个非法的 UTF-16 串，PostgreSQL 在编码成 UTF-8 时会<b>直接拒绝整条插入</b>。
    /// 一个防「写库失败」的类自己造出写库失败，就没有意义了。
    /// </para>
    /// </remarks>
    private static string Cut(string text, int maxLength)
    {
        if (text.Length <= maxLength)
            return text;

        var end = maxLength;
        if (char.IsHighSurrogate(text[end - 1]))
            end--;

        return text[..end];
    }
}
