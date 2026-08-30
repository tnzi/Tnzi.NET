namespace Tnzi.Notification.Services;

/// <summary>
/// 把一条传真回执落到对应的收件人上。
/// </summary>
/// <remarks>
/// <para>
/// ★ <b>与"怎么收到回执"完全分开</b>：本接口只管"已经有一条结论了，该怎么记"。
/// IMAP 轮询、网关 webhook、人工补录都调它，于是那条最要紧的逻辑（对号、只降级不升级、幂等）
/// 只有一份，不会因为来源不同而漂移。
/// </para>
/// <para>
/// ★★ <b>只降级不升级</b>：只有 <see cref="FaxDeliveryOutcome.Failed"/> 会改动数据
/// （<c>Sent</c> → <c>Failed</c> 并写明原因）。报"已送达"的回执什么也不做 ——
/// 那份传真本来就已经记成 <c>Sent</c>，没有新信息。这样一来，判读器认错的最坏后果是
/// 多出一条误报的失败（有人会去重发，对方收到两份），而不是把一份从没拨通的传真
/// 盖上"已送达"的章 —— 后者没有任何症状，几周后才会由对方说出来。
/// </para>
/// </remarks>
public interface IFaxConfirmationService
{
    /// <summary>
    /// 应用一条回执。找不到对应的收件人不算失败（收件箱里本来就有别人的信）。
    /// </summary>
    /// <param name="confirmation">回执结论。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>是否有一条收件人记录因此被改动。</returns>
    Task<Result<bool>> ApplyAsync(FaxConfirmation confirmation, CancellationToken cancellationToken = default);
}
