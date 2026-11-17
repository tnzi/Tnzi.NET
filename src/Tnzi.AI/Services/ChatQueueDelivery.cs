namespace Tnzi.AI.Services;

/// <summary>
/// <see cref="IChatQueueDelivery"/> 的实现：线程的消息队列怎么往前走。
/// </summary>
/// <remarks>
/// <para>
/// <b>交给正在跑的一轮</b>（<see cref="ClaimForRunningTurnAsync"/>，由执行那一轮的副本调用）：等它的消息逐条认领成交出，碰到第一条
/// 不能在运行中交出的就停，次序不乱。agent 收下一条时（<see cref="MarkDeliveredAsync"/>）它作为用户消息写进线程；那一轮结束时，
/// 收下的行删掉，交出却没被收下的回到等待（被停下时扣住），与其余等这一轮的消息一样往前走。
/// </para>
/// <para>
/// <b>一轮结束时</b>（<see cref="AdvanceAsync"/>，由 <c>CliRunFinishedEvent</c> 的处理器、扫描、以及没有在跑的一轮时的排队与立即发送调用）：
/// 等这一轮的消息里有一条要立即发送的，它单独发出，其余改等它启动的那一轮；没有的话，这一轮被停下就全部扣住，
/// 否则全部合成一轮发出：正文按写下的先后以空行相接，附件按先后全部带上，模型取最后一条选了模型的，元数据由 <see cref="IChatQueueMetadataMerger"/> 合并
/// （默认：<c>AI:Chat:Queue:StrictestValues</c> 列出的键取几条里最严的值，权限一类的键必须这样合并；其余的键后写的为准）。
/// </para>
/// <para>
/// <b>发出</b>是先认领再发：每一行以「还在等待」为条件改成 <see cref="QueuedMessageState.Delivering"/>，认领到的一方才发，
/// 事件、扫描、别的副本同时处理同一条线程时不会发两遍。发出走 <see cref="IChatService.ChatStreamingAsync"/>、以排队的用户的身份，
/// 读到这一轮的 <c>run_started</c> 就放手（外部运行已经入队，自己跑下去）；内建 Agent 的一轮挂在请求上，要读到流尽它才跑完。
/// 发出成功删行（附件此时已作为新一轮的附件另存一份，排队的那份随之删掉）；被拒（Agent 策略、用量上限、模型不允许、元数据不合规、
/// 附件不合规、线程不在了）扣住并写下原因。
/// </para>
/// <para>
/// ★ <b>运行归属</b>：后台没有登录用户，入队靠 <c>ChatRequestDto.UserId</c> 把运行记到排队的用户名下（调度器在没有登录用户时
/// 以请求的 UserId 作为运行的 <c>CreatorId</c>）。线程归属在这里先核一遍：后台的线程服务与调度器没有当前用户，不做归属判定。
/// </para>
/// </remarks>
public class ChatQueueDelivery : IChatQueueDelivery
{
    /// <summary>合成一轮时消息之间的分隔。</summary>
    public const string MessageSeparator = "\n\n";

    private readonly IRepository<QueuedThreadMessage, Guid> _queue;
    private readonly IRepository<AgentAttachment, Guid> _attachments;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ICliAgentDispatcher _dispatcher;
    private readonly IOptionsMonitor<AiChatOptions> _chatOptions;
    private readonly ILogger<ChatQueueDelivery> _logger;
    private readonly ICurrentTenant? _currentTenant;
    private readonly IEnumerable<IChatQueueHandoverPolicy> _handoverPolicies;

    public ChatQueueDelivery(
        IRepository<QueuedThreadMessage, Guid> queue,
        IRepository<AgentAttachment, Guid> attachments,
        IServiceScopeFactory scopeFactory,
        ICliAgentDispatcher dispatcher,
        IOptionsMonitor<AiChatOptions> chatOptions,
        ILogger<ChatQueueDelivery> logger,
        ICurrentTenant? currentTenant = null,
        IEnumerable<IChatQueueHandoverPolicy>? handoverPolicies = null)
    {
        _queue = Check.NotNull(queue);
        _attachments = Check.NotNull(attachments);
        _handoverPolicies = handoverPolicies ?? [];
        _scopeFactory = Check.NotNull(scopeFactory);
        _dispatcher = Check.NotNull(dispatcher);
        _chatOptions = Check.NotNull(chatOptions);
        _logger = Check.NotNull(logger);
        _currentTenant = currentTenant;
    }

    /// <summary>扣住的原因：它等的（或交给的）那一轮被停下了（<see cref="ErrorCodes.QueuedTurnStopped"/>）。</summary>
    internal const string TurnStoppedReason = "The turn was stopped, so this was not sent.";

    /// <inheritdoc />
    public async Task<Guid?> AdvanceAsync(Guid threadId, Guid? finishedRunId, bool stopped, CancellationToken ct = default)
    {
        if (finishedRunId is { } finished)
        {
            await SettleHandedOverAsync(threadId, finished, stopped, ct);
        }

        var waiting = await _queue.AsQueryable()
            .Where(m => m.ThreadId == threadId && m.State == QueuedMessageState.Queued && m.AfterRunId == finishedRunId)
            .OrderBy(m => m.CreationTime)
            .ThenBy(m => m.Id)
            .ToListAsync(ct);
        if (waiting.Count == 0)
        {
            return null;
        }

        var first = waiting.FirstOrDefault(m => m.SendNow);
        var rest = waiting.Where(m => m != first).ToList();

        if (first is not null)
        {
            var started = await DeliverAsync([first], ct);
            if (started.Delivered && started.RunId is { } nextRun)
            {
                // 其余的等「立即发送」启动的那一轮结束
                await RepointAsync(rest, nextRun, ct);
                return nextRun;
            }

            // 它没发出去（扣住了），或者是内建 Agent 的一轮、已经跑完：其余的照这一轮结束的规矩走
            if (rest.Count == 0)
            {
                return started.RunId;
            }
        }

        if (stopped)
        {
            await HoldAsync(rest, TurnStoppedReason, ErrorCodes.QueuedTurnStopped, ct);
            return null;
        }

        var merged = await DeliverAsync(rest, ct);
        return merged.RunId;
    }

    /// <inheritdoc />
    public async Task<int> SweepAsync(CancellationToken ct = default)
    {
        var options = _chatOptions.CurrentValue.Queue;
        var now = DateTime.UtcNow;
        var handledThreads = new HashSet<Guid>();

        // 1. 发出途中卡住的：不知道发没发出去，扣住交给用户，不自动重发（重发可能让同一句话在线程里出现两次）
        var stuckBefore = now - options.DeliveringTimeout;
        var interrupted = await AcrossTenants()
            .Where(m => m.State == QueuedMessageState.Delivering && m.DeliveringSince < stuckBefore)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.State, QueuedMessageState.Held)
                .SetProperty(m => m.SendNow, false)
                .SetProperty(m => m.DeliveringSince, (DateTime?)null)
                .SetProperty(m => m.Failure, "The service stopped while this message was being sent, so it may not have reached the conversation.")
                .SetProperty(m => m.FailureCode, ErrorCodes.QueuedMessageInterrupted), ct);
        if (interrupted > 0)
        {
            _logger.LogWarning("Held {Count} queued messages whose sending was interrupted", interrupted);
        }

        // 2. 交给了一轮的（交出、已收下）：那一轮已经结束（结束事件丢了）就收尾，交出却没被收下的随之往前走
        handledThreads.UnionWith(await SweepHandedOverAsync(ct));

        // 3. 还在等的：按（线程, 等的那一轮）分组。读整行再在内存里分组：多租户关闭时 TenantId 不在模型里，投影它翻译不了
        var queued = await AcrossTenants()
            .AsNoTracking()
            .Where(m => m.State == QueuedMessageState.Queued)
            .ToListAsync(ct);
        var groups = queued
            .GroupBy(m => (m.ThreadId, m.AfterRunId))
            .Select(g => new { g.Key.ThreadId, g.Key.AfterRunId, g.First().TenantId, Oldest = g.Min(m => m.CreationTime) })
            .ToList();
        if (groups.Count == 0)
        {
            return handledThreads.Count;
        }

        var runIds = groups.Where(g => g.AfterRunId.HasValue).Select(g => g.AfterRunId!.Value).Distinct().ToList();
        var statuses = runIds.Count > 0 ? await _dispatcher.GetStatusesAsSystemAsync(runIds, ct) : new Dictionary<Guid, CliRunStatus>();

        var strandedBefore = now - options.StrandedAfter;
        var handled = handledThreads.Count;
        foreach (var group in groups)
        {
            bool stopped;
            if (group.AfterRunId is not { } runId)
            {
                // 本该当场发出：还新的那些可能正在当场发出的路上，不抢
                if (group.Oldest >= strandedBefore)
                {
                    continue;
                }

                stopped = false;
            }
            else
            {
                // 说不出运行状态的调度器：只靠结束事件
                if (statuses is null)
                {
                    continue;
                }

                if (statuses.TryGetValue(runId, out var status))
                {
                    if (!IsTerminal(status))
                    {
                        continue;
                    }

                    stopped = status == CliRunStatus.Cancelled;
                }
                else
                {
                    // 不存在的运行（随线程删掉、或从未有过）按正常结束算
                    stopped = false;
                }
            }

            try
            {
                using (ChangeTenant(group.TenantId))
                {
                    await AdvanceAsync(group.ThreadId, group.AfterRunId, stopped, ct);
                }

                handled++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // 一条线程出错不该让别的线程的消息也等下去
                _logger.LogError(ex, "Queue sweep could not advance thread {ThreadId} after run {RunId}", group.ThreadId, group.AfterRunId);
            }
        }

        return handled;
    }

    /// <summary>这次发出的结果：发出了没有，发出的一轮的运行 id（内建 Agent 的一轮没有）。</summary>
    private readonly record struct Delivery(bool Delivered, Guid? RunId);

    /// <summary>认领、核归属、作为一轮发出、删行；被拒时扣住并写下原因。</summary>
    private async Task<Delivery> DeliverAsync(IReadOnlyList<QueuedThreadMessage> messages, CancellationToken ct)
    {
        var claimed = await ClaimAsync(messages, ct);
        if (claimed.Count == 0)
        {
            return new Delivery(false, null);
        }

        var last = claimed[^1];
        var threadId = last.ThreadId;
        var userId = last.UserId;
        var ids = claimed.Select(m => m.Id).ToList();

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var services = scope.ServiceProvider;

            var threads = services.GetService<IAgentThreadService>();
            if (threads is null || !await threads.IsOwnerAsync(threadId, userId))
            {
                // 线程不在了（删掉了，或者主人换了）：消息没有地方可去
                await _queue.AsQueryable().Where(m => ids.Contains(m.Id)).ExecuteDeleteAsync(ct);
                await DeleteQueuedAttachmentsAsync(claimed, ct);
                _logger.LogInformation("Dropped {Count} queued messages of thread {ThreadId}: the thread is gone", ids.Count, threadId);
                return new Delivery(false, null);
            }

            var request = await BuildRequestAsync(services, claimed);
            var chat = services.GetRequiredService<IChatService>();
            var runId = await StartTurnAsync(chat, request);

            await _queue.AsQueryable().Where(m => ids.Contains(m.Id)).ExecuteDeleteAsync(CancellationToken.None);
            await DeleteQueuedAttachmentsAsync(claimed, CancellationToken.None);
            _logger.LogInformation(
                "Sent {Count} queued messages of thread {ThreadId} as one turn (run {RunId})", ids.Count, threadId, runId);
            return new Delivery(true, runId);
        }
        catch (BusinessException ex)
        {
            _logger.LogInformation(
                "Queued messages of thread {ThreadId} were refused ({Code}); holding them: {Message}", threadId, ex.Code, ex.Message);
            await HoldAsync(claimed, ex.Message, ex.Code, CancellationToken.None);
            return new Delivery(false, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "Queued messages of thread {ThreadId} could not be sent; holding them", threadId);
            await HoldAsync(claimed, "The message could not be sent.", ErrorCodes.ChatFailed, CancellationToken.None);
            return new Delivery(false, null);
        }
    }

    /// <summary>
    /// 开始这一轮：外部运行读到 <c>run_started</c> 就放手（运行已经入队、自己跑下去），内建 Agent 的一轮读到流尽。
    /// 准备阶段的拒绝（策略、用量上限、模型、元数据、入队失败）以 <see cref="BusinessException"/> 在第一个事件之前抛出。
    /// </summary>
    /// <remarks>
    /// 用 <see cref="CancellationToken.None"/>：认领之后半途放弃会让行停在 <see cref="QueuedMessageState.Delivering"/>，
    /// 而一轮也许已经开始；与其那样，不如把这一步做完（外部运行只到入队为止，很快）。
    /// </remarks>
    private static async Task<Guid?> StartTurnAsync(IChatService chat, ChatRequestDto request)
    {
        await foreach (var evt in chat.ChatStreamingAsync(request, CancellationToken.None))
        {
            if (evt.EventType == RunStreamEventTypes.RunStarted && evt.RunId is { } runId)
            {
                return runId;
            }
        }

        return null;
    }

    /// <summary>
    /// 合成这一轮的请求：正文按写下的先后以空行相接，附件按先后全部带上（以内联数据，经聊天附件的同一套规则再读一遍），
    /// 模型取最后一条选了模型的，元数据交给 <see cref="IChatQueueMetadataMerger"/>
    /// （默认：<c>AI:Chat:Queue:StrictestValues</c> 列出的键取最严值，其余后写的为准）。
    /// </summary>
    private async Task<ChatRequestDto> BuildRequestAsync(IServiceProvider services, IReadOnlyList<QueuedThreadMessage> messages)
    {
        var attachments = await LoadAttachmentsAsync(messages, CancellationToken.None);
        var last = messages[^1];
        var parts = messages
            .Select(m => (IReadOnlyDictionary<string, JsonElement>?)ChatTurnMetadata.Parse(m.MetadataJson) ?? new Dictionary<string, JsonElement>())
            .ToList();
        var merger = services.GetService<IChatQueueMetadataMerger>() ?? new ConfiguredChatQueueMetadataMerger(_chatOptions);
        var merged = await merger.MergeAsync(new ChatQueueMergeContext(last.ThreadId, last.UserId, last.AgentId, parts), CancellationToken.None);

        return new ChatRequestDto
        {
            AgentId = last.AgentId,
            ThreadId = last.ThreadId,
            UserId = last.UserId,
            // 主人排下最后一条时就发了：线程在列表里那时已经挪过，这一轮开始时不再挪
            SentAt = last.CreationTime,
            Message = ChatQueueHandover.TextOrNames(
                string.Join(MessageSeparator, messages.Select(m => m.Text).Where(t => !string.IsNullOrWhiteSpace(t))),
                attachments.Select(a => (a.MediaType, a.FileName))),
            Content = attachments.Count == 0 ? null : attachments.Select(ToContentPart).ToList(),
            Model = messages.LastOrDefault(m => !string.IsNullOrWhiteSpace(m.Model))?.Model,
            Effort = MergedEffort(messages),
            Metadata = merged is { Count: > 0 } ? new Dictionary<string, JsonElement>(merged, StringComparer.Ordinal) : null
        };
    }

    /// <summary>
    /// 合成一轮的强度：最后一条选了强度的，但只在它不早于选定模型的那一条时（强度属于模型，早先那条的强度是按另一个模型选的）。
    /// </summary>
    internal static string? MergedEffort(IReadOnlyList<QueuedThreadMessage> messages)
    {
        var modelAt = -1;
        var effortAt = -1;
        for (var i = 0; i < messages.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(messages[i].Model))
            {
                modelAt = i;
            }

            if (!string.IsNullOrWhiteSpace(messages[i].Effort))
            {
                effortAt = i;
            }
        }

        return effortAt >= 0 && effortAt >= modelAt ? messages[effortAt].Effort : null;
    }

    /// <summary>逐行以「还在等待」为条件认领；返回认领到的那些，顺序不变。</summary>
    private async Task<List<QueuedThreadMessage>> ClaimAsync(IReadOnlyList<QueuedThreadMessage> messages, CancellationToken ct)
    {
        var claimed = new List<QueuedThreadMessage>(messages.Count);
        var now = DateTime.UtcNow;
        foreach (var message in messages)
        {
            var id = message.Id;
            var won = await _queue.AsQueryable()
                .Where(m => m.Id == id && m.State == QueuedMessageState.Queued)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(m => m.State, QueuedMessageState.Delivering)
                    .SetProperty(m => m.DeliveringSince, now), ct);
            if (won == 1)
            {
                claimed.Add(message);
            }
        }

        return claimed;
    }

    /// <summary>扣住：不再自己发出，只等用户立即发送。</summary>
    private async Task HoldAsync(IReadOnlyList<QueuedThreadMessage> messages, string? failure, string? failureCode, CancellationToken ct)
    {
        if (messages.Count == 0)
        {
            return;
        }

        var ids = messages.Select(m => m.Id).ToList();
        var reason = failure is { Length: > QueuedThreadMessage.FailureMaxLength }
            ? failure[..QueuedThreadMessage.FailureMaxLength]
            : failure;
        var code = failureCode is { Length: > QueuedThreadMessage.FailureCodeMaxLength } ? null : failureCode;

        await _queue.AsQueryable()
            .Where(m => ids.Contains(m.Id) && m.State != QueuedMessageState.Held)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.State, QueuedMessageState.Held)
                .SetProperty(m => m.SendNow, false)
                .SetProperty(m => m.DeliveringSince, (DateTime?)null)
                .SetProperty(m => m.Failure, reason)
                .SetProperty(m => m.FailureCode, code), ct);
    }

    private async Task RepointAsync(IReadOnlyList<QueuedThreadMessage> messages, Guid runId, CancellationToken ct)
    {
        if (messages.Count == 0)
        {
            return;
        }

        var ids = messages.Select(m => m.Id).ToList();
        await _queue.AsQueryable()
            .Where(m => ids.Contains(m.Id) && m.State == QueuedMessageState.Queued)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.AfterRunId, (Guid?)runId), ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<QueuedHandover>> ClaimForRunningTurnAsync(
        Guid threadId, Guid runId, bool acceptsAttachments, CancellationToken ct = default)
    {
        var waiting = await AcrossTenants()
            .AsNoTracking()
            .Where(m => m.ThreadId == threadId && m.State == QueuedMessageState.Queued && m.AfterRunId == runId)
            .OrderByDescending(m => m.SendNow)
            .ThenBy(m => m.CreationTime)
            .ThenBy(m => m.Id)
            .ToListAsync(ct);
        if (waiting.Count == 0)
        {
            return [];
        }

        var options = _chatOptions.CurrentValue.Queue;
        // 这一轮的强度只在有消息选了强度时才要比
        var runEffort = waiting.Any(m => !string.IsNullOrWhiteSpace(m.Effort))
            ? await _dispatcher.GetRunEffortAsSystemAsync(runId, ct)
            : null;
        var claimed = new List<QueuedHandover>();
        var now = DateTime.UtcNow;
        foreach (var message in waiting)
        {
            var attachments = await LoadAttachmentsAsync([message], ct);
            var goes = (acceptsAttachments || attachments.Count == 0)
                       && await ChatQueueHandover.CanGoMidTurnAsync(
                           message, ChatQueueHandover.HasFiles(attachments.Select(a => a.MediaType)), runId, runEffort, options, _handoverPolicies, ct);
            if (!goes)
            {
                // 次序不乱：它与它后面的消息一起等这一轮结束
                break;
            }

            var id = message.Id;
            var won = await AcrossTenants()
                .Where(m => m.Id == id && m.State == QueuedMessageState.Queued && m.AfterRunId == runId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(m => m.State, QueuedMessageState.HandedOver)
                    .SetProperty(m => m.HandedToRunId, (Guid?)runId)
                    .SetProperty(m => m.HandedOverAt, (DateTime?)now)
                    .SetProperty(m => m.SendNow, false), ct);
            if (won == 0)
            {
                // 刚被改了、删了或认领走了：后面的不越过它
                break;
            }

            claimed.Add(new QueuedHandover(
                message.Id,
                ChatQueueHandover.TextOrNames(message.Text, attachments.Select(a => (a.MediaType, a.FileName))),
                attachments.Select(a => new QueuedHandoverAttachment(a.MediaType, a.FileName, a.Content)).ToList()));
        }

        return claimed;
    }

    /// <inheritdoc />
    public async Task ReturnUntakenAsync(IReadOnlyCollection<Guid> messageIds, CancellationToken ct = default)
    {
        Check.NotNull(messageIds);
        if (messageIds.Count == 0)
        {
            return;
        }

        await AcrossTenants()
            .Where(m => messageIds.Contains(m.Id) && m.State == QueuedMessageState.HandedOver)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.State, QueuedMessageState.Queued)
                .SetProperty(m => m.HandedToRunId, (Guid?)null)
                .SetProperty(m => m.HandedOverAt, (DateTime?)null), ct);
    }

    /// <inheritdoc />
    public async Task<Guid?> MarkDeliveredAsync(Guid messageId, CancellationToken ct = default)
    {
        var message = await AcrossTenants().AsNoTracking().FirstOrDefaultAsync(m => m.Id == messageId, ct);
        if (message is not { State: QueuedMessageState.HandedOver })
        {
            return null;
        }

        using var scope = _scopeFactory.CreateScope();
        var threads = scope.ServiceProvider.GetService<IAgentThreadInternalService>();
        if (threads is null)
        {
            return null;
        }

        var attachmentIds = ChatQueueHandover.ParseIds(message.AttachmentIds);
        var described = attachmentIds.Count == 0
            ? []
            : await _attachments.AsQueryable().IgnoreQueryFilters()
                .Where(a => attachmentIds.Contains(a.Id))
                .Select(a => new { a.MediaType, a.FileName })
                .ToListAsync(ct);

        var threadMessageId = SequentialGuid.NewGuid();
        var saved = await threads.SaveUserMessageAsync(
            message.ThreadId,
            ChatQueueHandover.TextOrNames(message.Text, described.Select(a => (a.MediaType, a.FileName))),
            ChatTurnMetadata.Parse(message.MetadataJson),
            threadMessageId,
            sentAt: message.CreationTime,
            ct: ct);
        if (saved == Guid.Empty)
        {
            return null;
        }

        if (attachmentIds.Count > 0)
        {
            await _attachments.AsQueryable().IgnoreQueryFilters()
                .Where(a => attachmentIds.Contains(a.Id) && a.MessageId == null)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.MessageId, (Guid?)saved), ct);
        }

        await AcrossTenants()
            .Where(m => m.Id == messageId && m.State == QueuedMessageState.HandedOver)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.State, QueuedMessageState.Delivered)
                .SetProperty(m => m.ThreadMessageId, (Guid?)saved), ct);
        _logger.LogInformation(
            "Queued message {MessageId} of thread {ThreadId} was taken in by run {RunId} as thread message {ThreadMessageId}",
            messageId, message.ThreadId, message.HandedToRunId, saved);
        return saved;
    }

    /// <inheritdoc />
    public async Task HoldUntakenAsync(Guid messageId, string reason, CancellationToken ct = default)
    {
        var failure = reason.Length > QueuedThreadMessage.FailureMaxLength ? reason[..QueuedThreadMessage.FailureMaxLength] : reason;
        await AcrossTenants()
            .Where(m => m.Id == messageId && m.State == QueuedMessageState.HandedOver)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.State, QueuedMessageState.Held)
                .SetProperty(m => m.HandedToRunId, (Guid?)null)
                .SetProperty(m => m.HandedOverAt, (DateTime?)null)
                .SetProperty(m => m.Failure, failure)
                .SetProperty(m => m.FailureCode, ErrorCodes.QueuedMessageNotTaken), ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<QueuedHandover>> GetHandedOverAsync(Guid runId, CancellationToken ct = default)
    {
        var handed = await AcrossTenants()
            .AsNoTracking()
            .Where(m => m.HandedToRunId == runId
                        && (m.State == QueuedMessageState.HandedOver || m.State == QueuedMessageState.Delivered))
            .OrderBy(m => m.HandedOverAt)
            .ThenBy(m => m.Id)
            .ToListAsync(ct);

        var result = new List<QueuedHandover>(handed.Count);
        foreach (var message in handed)
        {
            var attachments = await LoadAttachmentsAsync([message], ct);
            result.Add(new QueuedHandover(
                message.Id,
                ChatQueueHandover.TextOrNames(message.Text, attachments.Select(a => (a.MediaType, a.FileName))),
                attachments.Select(a => new QueuedHandoverAttachment(a.MediaType, a.FileName, a.Content)).ToList()));
        }

        return result;
    }

    /// <summary>
    /// 交给运行 <paramref name="runId"/> 的消息在它结束时收尾：收下的删掉（它们已经是线程里的用户消息），没被收下的回到等这一轮
    /// （随后与其余等它的消息一起往前走）；这一轮被停下时扣住（停下的意思是「不要这样继续」）。
    /// </summary>
    private async Task SettleHandedOverAsync(Guid threadId, Guid runId, bool stopped, CancellationToken ct)
    {
        await AcrossTenants()
            .Where(m => m.ThreadId == threadId && m.HandedToRunId == runId && m.State == QueuedMessageState.Delivered)
            .ExecuteDeleteAsync(ct);

        var untaken = AcrossTenants()
            .Where(m => m.ThreadId == threadId && m.HandedToRunId == runId && m.State == QueuedMessageState.HandedOver);
        var count = stopped
            ? await untaken.ExecuteUpdateAsync(s => s
                .SetProperty(m => m.State, QueuedMessageState.Held)
                .SetProperty(m => m.HandedToRunId, (Guid?)null)
                .SetProperty(m => m.HandedOverAt, (DateTime?)null)
                .SetProperty(m => m.Failure, TurnStoppedReason)
                .SetProperty(m => m.FailureCode, ErrorCodes.QueuedTurnStopped), ct)
            : await untaken.ExecuteUpdateAsync(s => s
                .SetProperty(m => m.State, QueuedMessageState.Queued)
                .SetProperty(m => m.AfterRunId, (Guid?)runId)
                .SetProperty(m => m.HandedToRunId, (Guid?)null)
                .SetProperty(m => m.HandedOverAt, (DateTime?)null), ct);
        if (count > 0)
        {
            _logger.LogInformation(
                "{Count} queued messages handed to run {RunId} of thread {ThreadId} were not taken in before it ended; {Outcome}",
                count, runId, threadId, stopped ? "holding them" : "sending them as the next turn");
        }
    }

    /// <summary>交给了一轮、而那一轮已经结束（或不存在）的行：按那一轮的结局收尾。返回处理过的线程。</summary>
    private async Task<IReadOnlyCollection<Guid>> SweepHandedOverAsync(CancellationToken ct)
    {
        var handed = await AcrossTenants()
            .AsNoTracking()
            .Where(m => (m.State == QueuedMessageState.HandedOver || m.State == QueuedMessageState.Delivered) && m.HandedToRunId != null)
            .ToListAsync(ct);
        if (handed.Count == 0)
        {
            return [];
        }

        var runIds = handed.Select(m => m.HandedToRunId!.Value).Distinct().ToList();
        if (await _dispatcher.GetStatusesAsSystemAsync(runIds, ct) is not { } statuses)
        {
            return [];
        }

        var threads = new HashSet<Guid>();
        foreach (var group in handed.GroupBy(m => (m.ThreadId, RunId: m.HandedToRunId!.Value)))
        {
            var known = statuses.TryGetValue(group.Key.RunId, out var status);
            if (known && !IsTerminal(status))
            {
                continue;
            }

            try
            {
                using (ChangeTenant(group.First().TenantId))
                {
                    await AdvanceAsync(group.Key.ThreadId, group.Key.RunId, stopped: known && status == CliRunStatus.Cancelled, ct);
                }

                threads.Add(group.Key.ThreadId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Queue sweep could not settle the messages handed to run {RunId} of thread {ThreadId}",
                    group.Key.RunId, group.Key.ThreadId);
            }
        }

        return threads;
    }

    /// <summary>这些排队消息的附件（带字节），按消息的先后、再按附上的先后。</summary>
    private async Task<List<AgentAttachment>> LoadAttachmentsAsync(IReadOnlyList<QueuedThreadMessage> messages, CancellationToken ct)
    {
        var ids = messages.SelectMany(m => ChatQueueHandover.ParseIds(m.AttachmentIds)).ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var rows = await _attachments.AsQueryable().IgnoreQueryFilters().Where(a => ids.Contains(a.Id)).ToListAsync(ct);
        var byId = rows.ToDictionary(a => a.Id);
        return ids.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
    }

    /// <summary>作为新一轮发出之后，排队时存的那份附件删掉：新的一轮已经把它们另存在自己的用户消息上。</summary>
    private async Task DeleteQueuedAttachmentsAsync(IReadOnlyList<QueuedThreadMessage> messages, CancellationToken ct)
    {
        var ids = messages.SelectMany(m => ChatQueueHandover.ParseIds(m.AttachmentIds)).ToList();
        if (ids.Count == 0)
        {
            return;
        }

        await _attachments.AsQueryable().IgnoreQueryFilters()
            .Where(a => ids.Contains(a.Id) && a.MessageId == null)
            .ExecuteDeleteAsync(ct);
    }

    /// <summary>存下的附件 → 请求里的内联内容块（图片是图片块，其余是文档块，与重新生成时还原附件的写法一样）。</summary>
    private static ContentPartDto ToContentPart(AgentAttachment attachment)
    {
        var data = Convert.ToBase64String(attachment.Content);
        return attachment.MediaType.StartsWith("image/", StringComparison.Ordinal)
            ? new ImageContentPartDto { Base64Data = data, MediaType = attachment.MediaType, FileName = attachment.FileName }
            : new DocumentContentPartDto { Base64Data = data, MediaType = attachment.MediaType, FileName = attachment.FileName };
    }

    /// <summary>跨租户读队列：扫描跑在没有当前租户的后台。</summary>
    private IQueryable<QueuedThreadMessage> AcrossTenants() => _queue.AsQueryable().IgnoreQueryFilters();

    private IDisposable? ChangeTenant(Guid? tenantId) => _currentTenant?.Change(tenantId);

    private static bool IsTerminal(CliRunStatus status)
        => status is not (CliRunStatus.Queued or CliRunStatus.Dispatched or CliRunStatus.Running);
}
