using Message = Tnzi.Notification.Entities.Message;

namespace Tnzi.Notification.Services.Internal;

/// <summary>
/// 群发过程中把已经发生的投递结果<b>分片落库</b>，并顺带推进消息的心跳时间戳。
/// </summary>
/// <remarks>
/// <para>
/// ★★ <b>它挡住的是重复投递，不是「少了一行状态」。</b>此前一次群发的全部收件人状态由
/// 循环<b>结束之后</b>那一次 <c>SaveChangesAsync</c> 一起落库，于是在整个循环期间：
/// </para>
/// <list type="number">
/// <item>库里每个收件人都还写着 <c>Pending</c>；</item>
/// <item>消息行的 <c>LastModificationTime</c> 停在进循环之前那一次写入。</item>
/// </list>
/// <para>
/// 而恢复扫描判定「卡住」的依据正是
/// <c>Status == Sending &amp;&amp; (LastModificationTime ?? CreationTime) &lt; cutoff</c>。
/// 一次一千人的串行群发跑过 <see cref="DispatchOptions.StuckAfterMinutes"/>（默认 15 分钟）
/// 就会被另一个作用域<b>合法地</b>接手，第二次 <c>SendAsync</c> 看到全部收件人仍是
/// <c>Pending</c> —— <b>已经发出去的那些全部再发一遍</b>。进程在循环中途退出是同一回事，
/// 只是重发的是整份名单。
/// </para>
/// <para>
/// 两件事因此必须在循环<b>内</b>发生，而它们是同一次保存的两面：
/// </para>
/// <list type="bullet">
/// <item><b>记账</b> —— 已投递的收件人落库，崩溃时的重发面收窄到最后一片；</item>
/// <item><b>心跳</b> —— 消息行的 <c>LastModificationTime</c> 推进，正在飞的批次不会被
/// 判成「卡住」。心跳靠 <c>UpdateAsync</c> 把实体整体置为 <c>Modified</c> 实现，
/// 审计拦截器随即把时间戳覆写成"现在"；<b>不能</b>只保存收件人 —— 那样消息行本身
/// 一个字段都没改，时间戳原地不动。</item>
/// </list>
/// <para>
/// ★ <b>两个触发条件都要</b>：按条数（<see cref="FlushEveryRecipients"/>）管的是崩溃时的
/// 重发面，按时长（<see cref="FlushInterval"/>）管的是心跳。只有条数时，一批 3 个收件人
/// 每个发 10 分钟的慢投递照样会被判成卡住；只有时长时，一次极快的大批量在崩溃后仍要重发
/// 整个时间窗内的人。
/// </para>
/// <para>
/// ★ 刻意<b>不做成配置项</b>：这两个值不是调优旋钮，是「重复投递窗口有多宽」的下限保证。
/// 暴露出去就会有部署把它调大到与 <c>StuckAfterMinutes</c> 同量级，而那种失效毫无症状。
/// </para>
/// </remarks>
internal sealed class SendProgress
{
    /// <summary>攒够这么多个收件人就落一次库。</summary>
    internal const int FlushEveryRecipients = 20;

    /// <summary>距上次落库超过这么久就落一次库（即使还没攒够条数）。</summary>
    internal static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(30);

    private readonly IRepository<Message, Guid> _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly Message _message;

    private int _sinceLastFlush;
    private DateTime _lastFlushUtc = DateTime.UtcNow;

    public SendProgress(IRepository<Message, Guid> repository, IUnitOfWork unitOfWork, Message message)
    {
        _repository = Check.NotNull(repository);
        _unitOfWork = Check.NotNull(unitOfWork);
        _message = Check.NotNull(message);
    }

    /// <summary>
    /// 记下「又处理完一个收件人」，到点就落库。每个收件人处理完调一次。
    /// </summary>
    public async Task RecordAsync(CancellationToken cancellationToken)
    {
        _sinceLastFlush++;

        if (_sinceLastFlush < FlushEveryRecipients && DateTime.UtcNow - _lastFlushUtc < FlushInterval)
            return;

        await FlushAsync(cancellationToken);
    }

    /// <summary>
    /// 立即把当前进度落库：重算两个计数、把消息置为已修改（推进心跳）、保存。
    /// </summary>
    /// <remarks>
    /// 计数在这里重算而不是在调用方累加：<c>Recipients</c> 里可能有本轮之前就已经
    /// <c>Sent</c> 的人（续发场景），按集合数出来的才是这条消息真实的成败分布。
    /// </remarks>
    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        _message.SuccessCount = _message.Recipients.Count(r => r.Status == NotificationStatus.Sent);
        _message.FailureCount = _message.Recipients.Count(r => r.Status == NotificationStatus.Failed);

        await _repository.UpdateAsync(_message, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _sinceLastFlush = 0;
        _lastFlushUtc = DateTime.UtcNow;
    }
}
