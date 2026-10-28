using Message = Tnzi.Notification.Entities.Message;

namespace Tnzi.Notification.Services.Internal;

/// <summary>
/// 一条消息的 <see cref="Message.Status"/> 在有人争用时的全部写法：每一次都是一条条件更新。
/// </summary>
/// <remarks>
/// <para>
/// ★★ <b>谁要发一条消息，谁就先抢到它。</b>发送的入口不止一个：排队的工作项、定时器到点入队的工作项、
/// 延迟重试、管理端的 <c>{id}/send</c>、派发恢复的几遍扫描。它们看到的是同一行，而 <c>SendAsync</c>
/// 一旦开跑就会把所有 <c>Pending</c> / <c>Failed</c> 的收件人发一遍：两个入口各跑一次就是重复投递。
/// 读出来再改（read-modify-write）挡不住这件事：两边都读到 <c>Pending</c>，两边都写 <c>Sending</c>，两边都发。
/// </para>
/// <para>
/// ★★ <b>抢到之后，状态列只由条件更新改写。</b>发送循环在整个群发期间都拿着一个被跟踪的实体，
/// 而管理端的取消、批量删除写的是同一行：循环此前每次分片落库都把整行置为已修改再保存，内存里的
/// <c>Sending</c> 与 <c>IsDeleted = false</c> 于是盖掉了取消与删除，取消答复成功、剩下的人照发不误。
/// 现在循环只按属性保存自己改过的列（收件人状态、两个计数），心跳、收尾与取消各是一条带条件的更新，
/// 条件不成立就说明这一行已经不归这一轮了。
/// </para>
/// <para>
/// ★ <b>条件写在 UPDATE 里</b>：<c>UPDATE … SET … WHERE Id = @id AND Status = @expected</c>，影响行数为 1 才算数。
/// 把条件留在先前那次 SELECT 上等于两边都认为自己说了算。
/// 绕开变更跟踪器、不经 <c>IUnitOfWork</c>：每一条都是独立的原子写，要在下一步之前就对其它实例与其它作用域可见。
/// 软删除的行被全局过滤器挡在条件之外，所以删掉的消息与取消的消息一样「不再归这一轮」。
/// </para>
/// </remarks>
internal static class MessageClaim
{
    /// <summary>
    /// 把消息从 <paramref name="from"/> 抢到 <see cref="NotificationStatus.Sending"/>；抢到返回 <see langword="true"/>。
    /// </summary>
    /// <remarks>顺带把 <c>LastModificationTime</c> 推到现在，卡住扫描的计时从认领那一刻重新开始。</remarks>
    public static async Task<bool> TryClaimAsync(
        IRepository<Message, Guid> repository, Guid id, NotificationStatus from, CancellationToken cancellationToken)
    {
        var affected = await repository.AsQueryable()
            .Where(m => m.Id == id && m.Status == from)
            .ExecuteUpdateAsync(
                s => s.SetProperty(m => m.Status, NotificationStatus.Sending)
                      .SetProperty(m => m.LastModificationTime, DateTime.UtcNow),
                cancellationToken);

        return affected == 1;
    }

    /// <summary>这条消息是否仍在发送中（没被取消、没被删除）。发送循环每投递一个收件人之前问一次。</summary>
    public static Task<bool> IsSendingAsync(
        IRepository<Message, Guid> repository, Guid id, CancellationToken cancellationToken)
        => repository.AsQueryable()
            .AnyAsync(m => m.Id == id && m.Status == NotificationStatus.Sending, cancellationToken);

    /// <summary>
    /// 推进正在发送的消息的心跳（<c>LastModificationTime</c>）；它已不在发送中时返回 <see langword="false"/>。
    /// </summary>
    public static async Task<bool> TryHeartbeatAsync(
        IRepository<Message, Guid> repository, Guid id, CancellationToken cancellationToken)
    {
        var affected = await repository.AsQueryable()
            .Where(m => m.Id == id && m.Status == NotificationStatus.Sending)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.LastModificationTime, DateTime.UtcNow), cancellationToken);

        return affected == 1;
    }

    /// <summary>
    /// 一轮发送结束：把结局写回，只在消息仍在发送中时生效。它在这一轮里被取消或删除了就返回 <see langword="false"/>，
    /// 什么都不写。
    /// </summary>
    public static async Task<bool> TryFinishAsync(
        IRepository<Message, Guid> repository, Guid id, NotificationStatus outcome, DateTime? sentTime, string? failureReason,
        CancellationToken cancellationToken)
    {
        var affected = await repository.AsQueryable()
            .Where(m => m.Id == id && m.Status == NotificationStatus.Sending)
            .ExecuteUpdateAsync(
                s => s.SetProperty(m => m.Status, outcome)
                      .SetProperty(m => m.SentTime, sentTime)
                      .SetProperty(m => m.FailureReason, failureReason)
                      .SetProperty(m => m.LastModificationTime, DateTime.UtcNow),
                cancellationToken);

        return affected == 1;
    }

    /// <summary>
    /// 把消息从 <paramref name="from"/> 改成 <paramref name="to"/>，只在它此刻仍是 <paramref name="from"/> 时生效。
    /// </summary>
    public static async Task<bool> TryTransitionAsync(
        IRepository<Message, Guid> repository, Guid id, NotificationStatus from, NotificationStatus to,
        CancellationToken cancellationToken)
    {
        var affected = await repository.AsQueryable()
            .Where(m => m.Id == id && m.Status == from)
            .ExecuteUpdateAsync(
                s => s.SetProperty(m => m.Status, to)
                      .SetProperty(m => m.LastModificationTime, DateTime.UtcNow),
                cancellationToken);

        return affected == 1;
    }
}
