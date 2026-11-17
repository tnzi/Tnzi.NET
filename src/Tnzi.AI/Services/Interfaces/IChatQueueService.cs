namespace Tnzi.AI.Services;

/// <summary>
/// 线程的消息队列，面向线程主人：一轮还在跑时写下的消息在服务端排队，交给正在跑的那一轮（收得下运行中消息的 CLI），
/// 或者在那一轮结束时合成一轮发出。
/// </summary>
/// <remarks>
/// <para>
/// 每个方法都只对线程主人开放：线程不存在或不是 <c>userId</c> 的一律 404 <c>AI_THREAD_NOT_FOUND</c>，消息不在这条线程里 404
/// <c>AI_QUEUED_MESSAGE_NOT_FOUND</c>（与「不是你的」同一个答案）。
/// </para>
/// <para>
/// 发出的一面（运行结束时合成一轮、被停下时扣住、扫描补发）在 <see cref="IChatQueueDelivery"/>。
/// </para>
/// </remarks>
public interface IChatQueueService
{
    /// <summary>线程里排队的消息，按发出的先后：要立即发送的那条在最前，其余按写下的顺序。</summary>
    Task<Result<List<QueuedMessageDto>>> GetListAsync(Guid threadId, Guid userId, CancellationToken ct = default);

    /// <summary>
    /// 排一条消息。线程上有在跑的一轮时，它交给那一轮（执行它的副本随即写进进程，状态变成 <see cref="QueuedMessageState.HandedOver"/>），
    /// 那一轮收不下时等它结束；没有在跑的一轮时当场发出（返回的状态是 <see cref="QueuedMessageState.Delivering"/>，
    /// 这一行随即不在列表里了；发出被拒时是 <see cref="QueuedMessageState.Held"/>，带原因）。
    /// 附件（<see cref="QueueMessageRequestDto.Content"/>）按聊天附件的规则校验后存下。
    /// 没有正文也没有附件、或正文过长 400 <c>AI_INVALID_CONTENT</c>，附件不合规按聊天附件的错误（413 / 415 / 400），
    /// 元数据不合规 400 <c>AI_INVALID_TURN_METADATA</c>，线程里已排满 409 <c>AI_QUEUE_FULL</c>。
    /// </summary>
    Task<Result<QueuedMessageDto>> AddAsync(Guid threadId, Guid userId, QueueMessageRequestDto input, CancellationToken ct = default);

    /// <summary>
    /// 改一条排队消息的正文（等待中或扣住的都能改）；正在发出、已经交给 agent 或已被收下时 409 <c>AI_QUEUED_MESSAGE_DELIVERING</c>。
    /// </summary>
    Task<Result<QueuedMessageDto>> UpdateAsync(Guid threadId, Guid messageId, Guid userId, UpdateQueuedMessageDto input, CancellationToken ct = default);

    /// <summary>删掉一条排队消息（连同它的附件）；正在发出、已经交给 agent 或已被收下时 409 <c>AI_QUEUED_MESSAGE_DELIVERING</c>。</summary>
    Task<Result> RemoveAsync(Guid threadId, Guid messageId, Guid userId, CancellationToken ct = default);

    /// <summary>
    /// 立即发送。正在跑的那一轮收得下它（<see cref="QueuedMessageDto.SendNowStopsTurn"/> 为 false）时，它排到最前、马上交给那一轮，
    /// 什么都不停；收不下时停掉线程上正在跑的一轮，这条消息单独作为下一轮发出，其余等待的消息等它启动的那一轮结束。
    /// 没有在跑的一轮时当场发出。扣住的消息只有经这里才会发出。正在发出、已经交给 agent 或已被收下时 409 <c>AI_QUEUED_MESSAGE_DELIVERING</c>。
    /// </summary>
    Task<Result<QueuedMessageDto>> SendNowAsync(Guid threadId, Guid messageId, Guid userId, CancellationToken ct = default);
}

/// <summary>
/// 线程消息队列的发出一面：交给正在跑的一轮（执行它的副本调用），一轮结束时把等它的消息合成一轮发出，被停下时扣住，
/// 扫描补上漏掉的结束事件与发出途中断掉的消息。
/// </summary>
/// <remarks>
/// 发出走 <see cref="IChatService"/>，以排队那个用户的身份（<c>ChatRequestDto.UserId</c>）：Agent 策略、用量上限、允许的模型、
/// 元数据的限制与这个用户自己发消息时一样；被拒的消息扣住，带上原因。消息一旦作为新一轮发出，排队的行就删掉。
/// 多个副本、事件与扫描同时处理同一条线程是安全的：每一行以「还在等待」为条件认领，认领到的一方才发。
/// </remarks>
public interface IChatQueueDelivery
{
    /// <summary>
    /// 线程上的一轮结束了：等它的消息往前走。<paramref name="stopped"/>（这一轮被停下）时扣住它们，
    /// 除非其中有一条要立即发送，那一条单独发出、其余改等它启动的一轮。
    /// </summary>
    /// <param name="threadId">线程。</param>
    /// <param name="finishedRunId">结束的那一轮；null = 不等任何一轮的消息（排队时没有在跑的），现在就发。</param>
    /// <param name="stopped">这一轮是被停下的（取消）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>这次发出的一轮的运行 id（内建 Agent 的一轮在返回前已经跑完，没有运行 id）；什么都没发时为 null。</returns>
    Task<Guid?> AdvanceAsync(Guid threadId, Guid? finishedRunId, bool stopped, CancellationToken ct = default);

    /// <summary>
    /// 扫一遍所有租户的队列：等的那一轮已经结束（或不存在）的消息往前走，本该当场发出却还在等的发出，发出途中卡住的扣住，
    /// 交给了一轮而那一轮已经结束的收尾。返回处理过的线程数。
    /// </summary>
    Task<int> SweepAsync(CancellationToken ct = default);

    /// <summary>
    /// 执行运行 <paramref name="runId"/> 的副本来取要交给它的消息：等这一轮、还在等待的消息按发出的先后（要立即发送的在最前）逐条认领成
    /// <see cref="QueuedMessageState.HandedOver"/>，碰到第一条不能在运行中交出的（带文件、应用不让，见 <see cref="IChatQueueHandoverPolicy"/>）
    /// 就停：它与它后面的消息等这一轮结束，次序不乱。
    /// </summary>
    /// <remarks>调用方接着把它们写进进程；写不进去的经 <see cref="ReturnUntakenAsync"/> 放回去。默认实现什么都不交。</remarks>
    /// <param name="threadId">线程。</param>
    /// <param name="runId">正在跑、收得下运行中消息的那一轮。</param>
    /// <param name="acceptsAttachments">这一轮的 CLI 收不收附件（图片与 PDF）；false 时带附件的消息也等这一轮结束（新的一轮照常校验、说明原因）。</param>
    /// <param name="ct">取消令牌。</param>
    Task<IReadOnlyList<QueuedHandover>> ClaimForRunningTurnAsync(
        Guid threadId, Guid runId, bool acceptsAttachments, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<QueuedHandover>>([]);

    /// <summary>认领了却没写进进程的消息（进程此刻不收）：放回等待，次序不变。</summary>
    Task ReturnUntakenAsync(IReadOnlyCollection<Guid> messageIds, CancellationToken ct = default) => Task.CompletedTask;

    /// <summary>
    /// agent 收下了交给它的这条消息：它作为一条用户消息写进线程（附件挂到这条消息上），状态变成 <see cref="QueuedMessageState.Delivered"/>。
    /// 返回写下的线程消息 id；消息已经不在（线程删了）或不是交出的状态时为 null。
    /// </summary>
    /// <remarks>
    /// 调用方先把 agent 收下它之前那一段回答写进线程，再调这里：消息的位置（线程里的次序）就是它被收下的那一刻。
    /// </remarks>
    Task<Guid?> MarkDeliveredAsync(Guid messageId, CancellationToken ct = default) => Task.FromResult<Guid?>(null);

    /// <summary>CLI 明确不收这条交给它的消息（拒绝、会话结束时还没轮到）：扣住，写下原因，用户可以立即发送它。</summary>
    Task HoldUntakenAsync(Guid messageId, string reason, CancellationToken ct = default) => Task.CompletedTask;

    /// <summary>
    /// 交给了运行 <paramref name="runId"/>、那一轮还没结束的消息（<see cref="QueuedMessageState.HandedOver"/> 与
    /// <see cref="QueuedMessageState.Delivered"/>），内容与交出时相同。默认实现一条都没有。
    /// </summary>
    /// <remarks>
    /// 服务端重启后接回那一轮的进程时用：重放要把停机前写进进程的消息按原来的位置再交给适配器一次（进程早已收到，那一行不再发），
    /// 适配器才认得出 CLI 之后对它们的报告。
    /// </remarks>
    Task<IReadOnlyList<QueuedHandover>> GetHandedOverAsync(Guid runId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<QueuedHandover>>([]);
}

/// <summary>交给正在跑的一轮的一条排队消息（<see cref="IChatQueueDelivery.ClaimForRunningTurnAsync"/>）。</summary>
/// <param name="MessageId">排队消息 id（交给 CLI 时也用作这条用户消息的 id，CLI 收下时据此认出它）。</param>
/// <param name="Text">正文；只有附件时是附件的名字。</param>
/// <param name="Attachments">随它交出的附件（只有图片与 PDF：带文件的消息不在运行中交出）。</param>
public sealed record QueuedHandover(Guid MessageId, string Text, IReadOnlyList<QueuedHandoverAttachment> Attachments);

/// <summary>随排队消息交出的一个附件。</summary>
/// <param name="MediaType">媒体类型（图片或 PDF）。</param>
/// <param name="FileName">文件名。</param>
/// <param name="Content">字节。</param>
public sealed record QueuedHandoverAttachment(string MediaType, string? FileName, byte[] Content);
