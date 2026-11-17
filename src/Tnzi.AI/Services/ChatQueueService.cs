namespace Tnzi.AI.Services;

/// <summary>
/// <see cref="IChatQueueService"/> 的实现：线程主人对自己线程的消息队列做的事。
/// </summary>
/// <remarks>
/// <para>
/// 一条消息等哪一轮，在排队这一刻定：线程上最新一条还没结束的外部运行（<see cref="ICliAgentDispatcher.GetActiveThreadRunIdsAsSystemAsync"/>）。
/// 那一轮收得下运行中的消息时，执行它的副本随即把消息交给它（<see cref="IChatQueueDelivery.ClaimForRunningTurnAsync"/>）；收不下的等它结束。
/// 没有在跑的一轮时消息当场发出。内建 Agent 的一轮挂在发起它的请求上，服务端看不见它在跑：内建 Agent 的线程上排队等于当场发出，
/// 界面在内建的一轮进行中不该排队（<c>@tnzi/ui-ai</c> 的队列只在外部运行进行中接管输入框）。
/// </para>
/// <para>
/// 排队时等的那一轮恰好在写入前后结束：结束事件可能先于这一行提交而落空，所以写入之后再看一眼它的状态，已经结束就当场往前走，
/// 不等扫描。
/// </para>
/// <para>
/// 附件按聊天附件的规则读（<see cref="ChatAttachmentRules"/>、<c>AI:Chat:Attachments</c>，文件要应用的 <see cref="ICliFileAttachmentPolicy"/> 放行），
/// 存进 <c>AgentAttachment</c>（挂在线程上、还没有消息、属于线程主人），刷新页面后照样在，经线程的附件端点读得到。
/// </para>
/// </remarks>
public class ChatQueueService : ApplicationService, IChatQueueService
{
    private readonly IRepository<QueuedThreadMessage, Guid> _queue;
    private readonly IRepository<AgentThread, Guid> _threads;
    private readonly IRepository<AgentAttachment, Guid> _attachments;
    private readonly IAgentThreadService _threadService;
    private readonly ICliAgentDispatcher _dispatcher;
    private readonly IChatQueueDelivery _delivery;
    private readonly IOptionsMonitor<AiChatOptions> _chatOptions;
    private readonly IChatQueuePolicy? _policy;
    private readonly IEnumerable<IChatQueueHandoverPolicy> _handoverPolicies;
    private readonly IEnumerable<ICliFileAttachmentPolicy> _filePolicies;
    private readonly IAgentThreadInternalService? _threadWriter;

    public ChatQueueService(
        IServiceProvider serviceProvider,
        IRepository<QueuedThreadMessage, Guid> queue,
        IRepository<AgentThread, Guid> threads,
        IRepository<AgentAttachment, Guid> attachments,
        IAgentThreadService threadService,
        ICliAgentDispatcher dispatcher,
        IChatQueueDelivery delivery,
        IOptionsMonitor<AiChatOptions> chatOptions,
        IChatQueuePolicy? policy = null,
        IEnumerable<IChatQueueHandoverPolicy>? handoverPolicies = null,
        IEnumerable<ICliFileAttachmentPolicy>? filePolicies = null,
        IAgentThreadInternalService? threadWriter = null)
        : base(serviceProvider)
    {
        _queue = Check.NotNull(queue);
        _threads = Check.NotNull(threads);
        _attachments = Check.NotNull(attachments);
        _threadService = Check.NotNull(threadService);
        _dispatcher = Check.NotNull(dispatcher);
        _delivery = Check.NotNull(delivery);
        _chatOptions = Check.NotNull(chatOptions);
        _policy = policy;
        _handoverPolicies = handoverPolicies ?? [];
        _filePolicies = filePolicies ?? [];
        _threadWriter = threadWriter;
    }

    /// <inheritdoc />
    public async Task<Result<List<QueuedMessageDto>>> GetListAsync(Guid threadId, Guid userId, CancellationToken ct = default)
    {
        if (!await _threadService.IsOwnerAsync(threadId, userId))
        {
            return ThreadNotFound<List<QueuedMessageDto>>();
        }

        // 已经交出去的在前（它们已经在 agent 手里），其余按发出的先后：要立即发送的那条在最前，然后按写下的顺序
        var rows = await _queue.AsQueryable()
            .Where(m => m.ThreadId == threadId)
            .OrderByDescending(m => m.State == QueuedMessageState.Delivered || m.State == QueuedMessageState.HandedOver)
            .ThenByDescending(m => m.SendNow)
            .ThenBy(m => m.CreationTime)
            .ThenBy(m => m.Id)
            .ToListAsync(ct);
        return Ok(await ToDtosAsync(rows, ct));
    }

    /// <inheritdoc />
    public async Task<Result<QueuedMessageDto>> AddAsync(Guid threadId, Guid userId, QueueMessageRequestDto input, CancellationToken ct = default)
    {
        Check.NotNull(input);
        if (!await _threadService.IsOwnerAsync(threadId, userId))
        {
            return ThreadNotFound<QueuedMessageDto>();
        }

        if (await RefusedByPolicyAsync(ChatQueueOperation.Queue, threadId, userId, null, ct) is { } refusal)
        {
            return Fail<QueuedMessageDto>(refusal.Message!, refusal.Code ?? 403, refusal.ErrorCode);
        }

        var options = _chatOptions.CurrentValue;
        var read = ChatAttachmentRules.Read(input.Content, options.Attachments, acceptFiles: true);
        if (!read.Succeeded)
        {
            return Fail<QueuedMessageDto>(read.Message!, read.Code ?? 400, read.ErrorCode);
        }

        var files = read.Data!;
        var text = ValidateText(input.Text, options.Queue, hasAttachments: files.Count > 0);
        if (!text.Succeeded)
        {
            return Fail<QueuedMessageDto>(text.Message!, text.Code ?? 400, text.ErrorCode);
        }

        var metadata = ChatTurnMetadata.Validate(input.Metadata, options.Metadata);
        if (!metadata.Succeeded)
        {
            return Fail<QueuedMessageDto>(metadata.Message!, metadata.Code ?? 400, metadata.ErrorCode);
        }

        var model = string.IsNullOrWhiteSpace(input.Model) ? null : input.Model.Trim();
        if (model is { Length: > QueuedThreadMessage.ModelMaxLength })
        {
            return Fail<QueuedMessageDto>("The model id is too long.", 400, ErrorCodes.InvalidContent);
        }

        var effort = string.IsNullOrWhiteSpace(input.Effort) ? null : input.Effort.Trim();
        if (effort is { Length: > QueuedThreadMessage.EffortMaxLength })
        {
            return Fail<QueuedMessageDto>("The reasoning effort is too long.", 400, ErrorCodes.InvalidContent);
        }

        var count = await _queue.AsQueryable().CountAsync(m => m.ThreadId == threadId, ct);
        if (count >= options.Queue.MaxMessagesPerThread)
        {
            return Fail<QueuedMessageDto>(
                $"This conversation already has {count} queued messages; wait for them to go or remove some.", 409, ErrorCodes.QueueFull);
        }

        var agentId = await _threads.AsQueryable().Where(t => t.Id == threadId).Select(t => t.AgentId).FirstOrDefaultAsync(ct);
        if (await RefusedFilesAsync(files, threadId, userId, agentId, ct) is { } refusedFiles)
        {
            return Fail<QueuedMessageDto>(refusedFiles.Message!, refusedFiles.Code ?? 415, refusedFiles.ErrorCode);
        }

        var afterRunId = (await _dispatcher.GetActiveThreadRunIdsAsSystemAsync(threadId, ct))?.LastOrDefault();
        var attachmentIds = await StoreAttachmentsAsync(files, threadId, userId, ct);

        var row = new QueuedThreadMessage
        {
            Id = SequentialGuid.NewGuid(),
            ThreadId = threadId,
            UserId = userId,
            AgentId = agentId,
            Text = text.Data!,
            MetadataJson = ChatTurnMetadata.Serialize(metadata.Data),
            Model = model,
            Effort = effort,
            State = QueuedMessageState.Queued,
            AfterRunId = afterRunId == Guid.Empty ? null : afterRunId,
            AttachmentIds = ChatQueueHandover.SerializeIds(attachmentIds)
        };
        await _queue.InsertAsync(row, ct);
        await _queue.SaveChangesAsync(ct);
        // 主人此刻发了一条消息，只是它要等着才进线程：列表现在就把这一行挪到该在的位置，送达时按同一个时间写，不再挪
        if (_threadWriter is not null)
        {
            await _threadWriter.MarkUserMessageAsync(threadId, row.CreationTime, ct);
        }

        // 往队列里放一条消息就是主人在这条线程里说了话：服务端建的、等着主人说话的线程从此进列表
        // （运行中交给进程的那一条不再经过聊天入口，只能在这里认）
        await ListOnUserMessageAsync(threadId, userId, ct);

        await CatchUpAsync(threadId, row.AfterRunId, ct);
        return Ok(await CurrentDtoAsync(row, ct));
    }

    private async Task ListOnUserMessageAsync(Guid threadId, Guid userId, CancellationToken ct)
    {
        if (ServiceProvider.GetService<IAgentThreadInternalService>() is not { } threads)
        {
            return;
        }

        try
        {
            await threads.ListOnUserMessageAsync(threadId, userId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 只是列表上的事：消息已经排上了，线程留在列表外，主人下一次说话再试
            Logger.LogWarning(ex, "Could not list thread {ThreadId} after its owner queued a message in it", threadId);
        }
    }

    /// <inheritdoc />
    public async Task<Result<QueuedMessageDto>> UpdateAsync(
        Guid threadId, Guid messageId, Guid userId, UpdateQueuedMessageDto input, CancellationToken ct = default)
    {
        Check.NotNull(input);
        if (!await _threadService.IsOwnerAsync(threadId, userId))
        {
            return ThreadNotFound<QueuedMessageDto>();
        }

        if (await RefusedByPolicyAsync(ChatQueueOperation.Edit, threadId, userId, messageId, ct) is { } refusal)
        {
            return Fail<QueuedMessageDto>(refusal.Message!, refusal.Code ?? 403, refusal.ErrorCode);
        }

        var existing = await FindAsync(threadId, messageId, ct);
        if (existing is null)
        {
            return MessageNotFound<QueuedMessageDto>();
        }

        var hasAttachments = ChatQueueHandover.ParseIds(existing.AttachmentIds).Count > 0;
        var text = ValidateText(input.Text, _chatOptions.CurrentValue.Queue, hasAttachments);
        if (!text.Succeeded)
        {
            return Fail<QueuedMessageDto>(text.Message!, text.Code ?? 400, text.ErrorCode);
        }

        var changed = await Changeable(_queue.AsQueryable().Where(m => m.Id == messageId && m.ThreadId == threadId))
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Text, text.Data!), ct);
        if (changed == 0)
        {
            return await NotChangeableAsync<QueuedMessageDto>(threadId, messageId, ct);
        }

        var row = await FindAsync(threadId, messageId, ct);
        return row is null ? MessageNotFound<QueuedMessageDto>() : Ok((await ToDtosAsync([row], ct))[0]);
    }

    /// <inheritdoc />
    public async Task<Result> RemoveAsync(Guid threadId, Guid messageId, Guid userId, CancellationToken ct = default)
    {
        if (!await _threadService.IsOwnerAsync(threadId, userId))
        {
            return Fail("Thread not found", 404, ErrorCodes.ThreadNotFound);
        }

        var existing = await FindAsync(threadId, messageId, ct);
        var removed = await Changeable(_queue.AsQueryable().Where(m => m.Id == messageId && m.ThreadId == threadId))
            .ExecuteDeleteAsync(ct);
        if (removed > 0)
        {
            // 附件随它走：它们还没挂到任何消息上，留着就是没人读得到的字节
            var attachmentIds = ChatQueueHandover.ParseIds(existing?.AttachmentIds);
            if (attachmentIds.Count > 0)
            {
                await _attachments.AsQueryable()
                    .Where(a => attachmentIds.Contains(a.Id) && a.MessageId == null)
                    .ExecuteDeleteAsync(ct);
            }

            return Ok();
        }

        var refused = await NotChangeableAsync<object>(threadId, messageId, ct);
        return Fail(refused.Message!, refused.Code ?? 404, refused.ErrorCode);
    }

    /// <inheritdoc />
    public async Task<Result<QueuedMessageDto>> SendNowAsync(Guid threadId, Guid messageId, Guid userId, CancellationToken ct = default)
    {
        if (!await _threadService.IsOwnerAsync(threadId, userId))
        {
            return ThreadNotFound<QueuedMessageDto>();
        }

        if (await RefusedByPolicyAsync(ChatQueueOperation.SendNow, threadId, userId, messageId, ct) is { } refusal)
        {
            return Fail<QueuedMessageDto>(refusal.Message!, refusal.Code ?? 403, refusal.ErrorCode);
        }

        var row = await FindAsync(threadId, messageId, ct);
        if (row is null)
        {
            return MessageNotFound<QueuedMessageDto>();
        }

        if (!IsChangeable(row.State))
        {
            return Delivering<QueuedMessageDto>();
        }

        var afterRunId = (await _dispatcher.GetActiveThreadRunIdsAsSystemAsync(threadId, ct))?.LastOrDefault();
        Guid? target = afterRunId is { } id && id != Guid.Empty ? id : null;

        // 正在跑的那一轮收得下它：排到最前交给那一轮，什么都不停（执行它的副本随即写进进程）
        var stopsTurn = target is { } running && await SendNowStopsTurnAsync(row, running, await TakesMessagesAsync(running, ct), ct);

        // 只能有一条排在最前；它与其余等待的消息一起改等线程上最新的那一轮（停下它之后，结束事件带着它们往前走）
        await _queue.AsQueryable()
            .Where(m => m.ThreadId == threadId && m.Id != messageId && m.State == QueuedMessageState.Queued)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.SendNow, false)
                .SetProperty(m => m.AfterRunId, target), ct);
        var marked = await Changeable(_queue.AsQueryable().Where(m => m.Id == messageId))
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.SendNow, true)
                .SetProperty(m => m.State, QueuedMessageState.Queued)
                .SetProperty(m => m.AfterRunId, target)
                .SetProperty(m => m.Failure, (string?)null)
                .SetProperty(m => m.FailureCode, (string?)null), ct);
        if (marked == 0)
        {
            return await NotChangeableAsync<QueuedMessageDto>(threadId, messageId, ct);
        }

        if (stopsTurn)
        {
            // 停掉线程上在跑的（不只是最新那一轮）：归属上面已经核过，系统取消不再按运行的创建者判定可见性
            var cancelled = await _dispatcher.CancelThreadRunsAsSystemAsync(threadId, reason: null, ct);
            if (!cancelled.Succeeded)
            {
                Logger.LogWarning(
                    "Send now could not stop the runs of thread {ThreadId}: {Message}", threadId, cancelled.Message);
            }
        }

        await CatchUpAsync(threadId, target, ct);
        return Ok(await CurrentDtoAsync(row, ct));
    }

    /// <summary>
    /// 应用的 <see cref="IChatQueuePolicy"/> 拒绝这次操作时的结果（说明齐全：没给状态码按 403，没给错误码按 <c>AI_QUEUE_NOT_ALLOWED</c>）；
    /// 放行或没有注册策略时为 null。
    /// </summary>
    private async Task<Result?> RefusedByPolicyAsync(
        ChatQueueOperation operation, Guid threadId, Guid userId, Guid? messageId, CancellationToken ct)
    {
        if (_policy is null)
        {
            return null;
        }

        var agentId = await _threads.AsQueryable().Where(t => t.Id == threadId).Select(t => t.AgentId).FirstOrDefaultAsync(ct);
        var verdict = await _policy.CanQueueAsync(new ChatQueueRequest(operation, threadId, userId, agentId, messageId), ct);
        if (verdict.Succeeded)
        {
            return null;
        }

        return Result.Failure(
            verdict.Message ?? "Messages cannot be queued from this session.",
            verdict.Code is >= 400 ? verdict.Code.Value : 403,
            verdict.ErrorCode ?? ErrorCodes.QueueNotAllowed);
    }

    /// <summary>
    /// 带着文件（不是图片或 PDF）的消息，应用的 <see cref="ICliFileAttachmentPolicy"/> 不收时的拒绝（415，与聊天发送同一句话）；
    /// 没有文件、或收下时为 null。线程不绑 Agent 时无从询问，文件一律不收。
    /// </summary>
    private async Task<Result?> RefusedFilesAsync(
        IReadOnlyList<ChatAttachmentPayload> attachments, Guid threadId, Guid userId, Guid? agentId, CancellationToken ct)
    {
        if (attachments.FirstOrDefault(a => a.IsFile) is not { } first)
        {
            return null;
        }

        var accepted = agentId is { } agent && await ChatAttachmentRules.AcceptsFilesAsync(
            _filePolicies,
            new CliFileAttachmentRequest
            {
                AgentId = agent,
                ThreadId = threadId,
                UserId = userId,
                Files = attachments.Where(a => a.IsFile)
                    .Select(a => new CliTurnAttachment(a.MediaType, a.FileName, a.Content.LongLength, IsFile: true))
                    .ToList()
            },
            ct);
        if (accepted)
        {
            return null;
        }

        var refusal = ChatAttachmentRules.RefuseFile(first);
        return Result.Failure(refusal.Message!, refusal.Code ?? 415, refusal.ErrorCode);
    }

    /// <summary>存下附件（挂在线程上、还没有消息、属于线程主人），返回它们的 id，次序不变。</summary>
    private async Task<IReadOnlyList<Guid>> StoreAttachmentsAsync(
        IReadOnlyList<ChatAttachmentPayload> attachments, Guid threadId, Guid userId, CancellationToken ct)
    {
        if (attachments.Count == 0)
        {
            return [];
        }

        var rows = attachments.Select(a => new AgentAttachment
        {
            Id = SequentialGuid.NewGuid(),
            ThreadId = threadId,
            OwnerId = userId,
            Origin = AttachmentOrigin.Upload,
            MediaType = a.MediaType,
            FileName = a.FileName,
            Size = a.Content.LongLength,
            Content = a.Content
        }).ToList();
        await _attachments.InsertManyAsync(rows, ct);
        await _attachments.SaveChangesAsync(ct);
        return rows.Select(r => r.Id).ToList();
    }

    /// <summary>
    /// 消息等的那一轮已经不在跑（或者没有在跑的一轮）：现在就往前走，而不是等一个也许已经发过了的结束事件。
    /// </summary>
    private async Task CatchUpAsync(Guid threadId, Guid? afterRunId, CancellationToken ct)
    {
        if (afterRunId is not { } runId)
        {
            await _delivery.AdvanceAsync(threadId, null, stopped: false, ct);
            return;
        }

        var statuses = await _dispatcher.GetStatusesAsSystemAsync([runId], ct);
        if (statuses is null)
        {
            return;
        }

        if (!statuses.TryGetValue(runId, out var status))
        {
            await _delivery.AdvanceAsync(threadId, runId, stopped: false, ct);
            return;
        }

        if (status is not (CliRunStatus.Queued or CliRunStatus.Dispatched or CliRunStatus.Running))
        {
            await _delivery.AdvanceAsync(threadId, runId, stopped: status == CliRunStatus.Cancelled, ct);
        }
    }

    /// <summary>这一行此刻的样子；已经作为一轮发出（行没了）时，是它发出那一刻的样子。</summary>
    private async Task<QueuedMessageDto> CurrentDtoAsync(QueuedThreadMessage row, CancellationToken ct)
    {
        var current = await FindAsync(row.ThreadId, row.Id, ct);
        if (current is not null)
        {
            return (await ToDtosAsync([current], ct))[0];
        }

        var sent = ToDto(row, []);
        sent.State = QueuedMessageState.Delivering;
        return sent;
    }

    /// <summary>
    /// 行 → 线缆上的形状，带上附件的说明（不带字节）与「立即发送会不会停下正在跑的一轮」：同一条线程的行共用一次运行查询。
    /// </summary>
    private async Task<List<QueuedMessageDto>> ToDtosAsync(IReadOnlyList<QueuedThreadMessage> rows, CancellationToken ct)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var idsByRow = rows.ToDictionary(r => r.Id, r => ChatQueueHandover.ParseIds(r.AttachmentIds));
        var allIds = idsByRow.Values.SelectMany(ids => ids).Distinct().ToList();
        var attachments = allIds.Count == 0
            ? new Dictionary<Guid, AttachmentDto>()
            : await _attachments.AsQueryable()
                .Where(a => allIds.Contains(a.Id))
                .Select(a => new AttachmentDto { Id = a.Id, Origin = a.Origin, MediaType = a.MediaType, FileName = a.FileName, Size = a.Size })
                .ToDictionaryAsync(a => a.Id, ct);

        var threadId = rows[0].ThreadId;
        var active = (await _dispatcher.GetActiveThreadRunIdsAsSystemAsync(threadId, ct))?.LastOrDefault();
        Guid? running = active is { } id && id != Guid.Empty ? id : null;
        var takes = running is { } run && await TakesMessagesAsync(run, ct);

        var dtos = new List<QueuedMessageDto>(rows.Count);
        foreach (var row in rows)
        {
            var listed = idsByRow[row.Id].Where(attachments.ContainsKey).Select(a => attachments[a]).ToList();
            var dto = ToDto(row, listed);
            dto.SendNowStopsTurn = running is { } target && IsChangeable(row.State)
                && await SendNowStopsTurnAsync(row, target, takes, ct, listed.Select(a => a.MediaType));
            dtos.Add(dto);
        }

        return dtos;
    }

    /// <summary>正在跑的那一轮收不收得下运行中的消息（调度器说不出来按收不下）。</summary>
    private async Task<bool> TakesMessagesAsync(Guid runId, CancellationToken ct)
        => await _dispatcher.TakesMessagesWhileRunningAsSystemAsync(runId, ct) == true;

    /// <summary>立即发送这条消息会不会停下正在跑的那一轮：那一轮收不下运行中的消息，或者这条消息不能在运行中交出。</summary>
    private async Task<bool> SendNowStopsTurnAsync(
        QueuedThreadMessage row, Guid runId, bool takesMessages, CancellationToken ct, IEnumerable<string>? mediaTypes = null)
    {
        if (!takesMessages)
        {
            return true;
        }

        mediaTypes ??= await AttachmentMediaTypesAsync(row, ct);
        var options = _chatOptions.CurrentValue.Queue;
        var runEffort = string.IsNullOrWhiteSpace(row.Effort) ? null : await _dispatcher.GetRunEffortAsSystemAsync(runId, ct);
        return !await ChatQueueHandover.CanGoMidTurnAsync(
            row, ChatQueueHandover.HasFiles(mediaTypes), runId, runEffort, options, _handoverPolicies, ct);
    }

    private async Task<List<string>> AttachmentMediaTypesAsync(QueuedThreadMessage row, CancellationToken ct)
    {
        var ids = ChatQueueHandover.ParseIds(row.AttachmentIds);
        return ids.Count == 0
            ? []
            : await _attachments.AsQueryable().Where(a => ids.Contains(a.Id)).Select(a => a.MediaType).ToListAsync(ct);
    }

    private Task<QueuedThreadMessage?> FindAsync(Guid threadId, Guid messageId, CancellationToken ct)
        => _queue.AsQueryable().AsNoTracking().FirstOrDefaultAsync(m => m.Id == messageId && m.ThreadId == threadId, ct);

    /// <summary>用户还能改、删、立即发送的状态：等待中与扣住的。正在发出、已经交给 agent、已被收下的不行。</summary>
    private static bool IsChangeable(QueuedMessageState state) => state is QueuedMessageState.Queued or QueuedMessageState.Held;

    private static IQueryable<QueuedThreadMessage> Changeable(IQueryable<QueuedThreadMessage> query)
        => query.Where(m => m.State == QueuedMessageState.Queued || m.State == QueuedMessageState.Held);

    /// <summary>改 / 删 / 立即发送没有落下：是已经在路上（409），还是根本不在（404）。</summary>
    private async Task<Result<T>> NotChangeableAsync<T>(Guid threadId, Guid messageId, CancellationToken ct)
    {
        var row = await FindAsync(threadId, messageId, ct);
        return row is null ? MessageNotFound<T>() : Delivering<T>();
    }

    private static Result<string> ValidateText(string? text, ChatQueueOptions options, bool hasAttachments)
    {
        var trimmed = text?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 && !hasAttachments)
        {
            return Result<string>.Failure("A queued message needs some text or an attachment.", 400, ErrorCodes.InvalidContent);
        }

        return trimmed.Length > options.MaxTextLength
            ? Result<string>.Failure($"A queued message can be at most {options.MaxTextLength} characters long.", 400, ErrorCodes.InvalidContent)
            : Result<string>.Success(trimmed);
    }

    private Result<T> ThreadNotFound<T>() => Fail<T>("Thread not found", 404, ErrorCodes.ThreadNotFound);

    private Result<T> MessageNotFound<T>() => Fail<T>("Queued message not found.", 404, ErrorCodes.QueuedMessageNotFound);

    private Result<T> Delivering<T>()
        => Fail<T>("This message is being sent right now.", 409, ErrorCodes.QueuedMessageDelivering);

    /// <summary>行 → 线缆上的形状（附件由调用方给出；<see cref="QueuedMessageDto.SendNowStopsTurn"/> 由调用方填）。</summary>
    public static QueuedMessageDto ToDto(QueuedThreadMessage row, List<AttachmentDto> attachments)
    {
        Check.NotNull(row);
        Check.NotNull(attachments);
        return new QueuedMessageDto
        {
            Id = row.Id,
            ThreadId = row.ThreadId,
            Text = row.Text,
            State = row.State,
            SendNow = row.SendNow,
            Failure = row.Failure,
            FailureCode = row.FailureCode,
            Model = row.Model,
            Effort = row.Effort,
            Metadata = ChatTurnMetadata.Parse(row.MetadataJson),
            AfterRunId = row.AfterRunId,
            QueuedTime = row.CreationTime,
            Attachments = attachments,
            HandedToRunId = row.HandedToRunId,
            ThreadMessageId = row.ThreadMessageId
        };
    }
}
