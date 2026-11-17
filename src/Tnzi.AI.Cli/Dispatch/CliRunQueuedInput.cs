using System.Globalization;

namespace Tnzi.AI.Cli.Dispatch;

/// <summary>
/// 线程消息队列与正在跑的这一轮之间的通道：用户在这一轮进行中排的消息由任一服务端副本落库（<c>QueuedThreadMessage</c>），
/// 执行这条运行的副本在这里轮询到它们，写进 CLI 进程（<see cref="ICliTurnInput"/>），agent 在两步之间收下、不必停下。
/// </summary>
/// <remarks>
/// <para>
/// 收下的着落从适配器的事件流里来（<see cref="CliAgentEventType.UserMessage"/>），执行器在主循环里把收下之前的那段回答与这条用户消息
/// 写进线程；这里只负责交出。认领了却写不进去的（进程此刻不收）放回等待，次序不变。
/// </para>
/// <para>
/// 进程停在两轮之间等后台任务时不往里写（那时没有一轮可以并进去）：等这一轮的消息直接作为新的一轮发出，同线程的新运行接过这个进程
/// （文档 §6.18 的交接），不必等后台任务。
/// </para>
/// <para>
/// <b>可重新附着的进程</b>（<see cref="CliQueuedInputRecord"/>）：每条写进去的消息连同写它那一刻的事件序号记在
/// <see cref="CliRun.HandedInputs"/>，先记、再写，都在序号闸门里（写的时候没有别的事件插进来）。接回进程时重放从第一行输出起，
/// 主循环处理完那个序号的事件就把这条消息原样再交给适配器（<see cref="ReplayRecordedAsync"/>）：进程早已收到这一行，重放的 transport
/// 按行数丢掉它，stdin 行数与适配器认得的消息都与停机前一样。重放追上之前不交新的消息：那时写出的行会顶掉一行旧的。
/// </para>
/// </remarks>
internal sealed class CliRunQueuedInput
{
    private readonly Guid _runId;
    private readonly Guid _threadId;
    private readonly ICliTurnInput _input;
    private readonly ICliTurnContinuation? _continuation;
    private readonly bool _acceptsAttachments;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeSpan _pollInterval;
    private readonly ILogger _logger;
    private readonly CliQueuedInputRecord? _record;

    /// <param name="runId">运行。</param>
    /// <param name="threadId">运行所在的线程。</param>
    /// <param name="input">这一次尝试的适配器。</param>
    /// <param name="continuation">同一个适配器的交接能力（说得出此刻是不是停在两轮之间）；没有时为 null。</param>
    /// <param name="acceptsAttachments">provider 收不收附件（图片、PDF）：不收时带附件的消息等这一轮结束。</param>
    /// <param name="scopeFactory">每次读库开一个作用域（同事件落库）。</param>
    /// <param name="pollInterval">多久查一次队列。</param>
    /// <param name="logger">日志。</param>
    /// <param name="record">进程可重新附着时的写入记录；null = 不记（进程随服务端一起结束）。</param>
    public CliRunQueuedInput(
        Guid runId, Guid threadId, ICliTurnInput input, ICliTurnContinuation? continuation, bool acceptsAttachments,
        IServiceScopeFactory scopeFactory, TimeSpan pollInterval, ILogger logger, CliQueuedInputRecord? record = null)
    {
        _record = record;
        _runId = runId;
        _threadId = threadId;
        _input = Check.NotNull(input);
        _continuation = continuation;
        _acceptsAttachments = acceptsAttachments;
        _scopeFactory = Check.NotNull(scopeFactory);
        _pollInterval = pollInterval > TimeSpan.Zero ? pollInterval : TimeSpan.FromSeconds(1);
        _logger = Check.NotNull(logger);
    }

    /// <summary>后台循环：查有没有等这一轮的消息，有就交出。随这一次尝试结束（<paramref name="cancellationToken"/>）。</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await HandOverAsync(cancellationToken);
                await Task.Delay(_pollInterval, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // 一次查库或写入失败不能让消息丢掉：写不进去的已经放回等待，下一轮再试
                _logger.LogWarning(ex, "Could not hand the queued messages of thread {ThreadId} to CLI run {RunId}; retrying", _threadId, _runId);
                await Task.Delay(_pollInterval, cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }
    }

    /// <summary>交出一次：认领等这一轮的消息、逐条写进进程。返回写进去的消息 id。</summary>
    internal async Task<IReadOnlyList<Guid>> HandOverAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        if (scope.ServiceProvider.GetService<IChatQueueDelivery>() is not { } delivery)
        {
            return [];
        }

        if (_continuation is { IsAwaitingFollowUp: true })
        {
            // 两轮之间：作为新的一轮发出，同线程的新运行接过这个进程；没有等这一轮的消息时什么都不做
            await delivery.AdvanceAsync(_threadId, _runId, stopped: false, cancellationToken);
            return [];
        }

        if (_record is { } record && !record.MayWrite())
        {
            // 这一轮还没有事件、或重放还没追上：现在认领只会原样放回去（列表上来回闪一下）。这里不持闸门只是预判，写之前在闸门里再判一次
            return [];
        }

        var claimed = await delivery.ClaimForRunningTurnAsync(_threadId, _runId, _acceptsAttachments, cancellationToken);
        var written = new List<Guid>(claimed.Count);
        try
        {
            foreach (var message in claimed)
            {
                var attachments = message.Attachments
                    .Select(a => new CliPromptAttachment(a.MediaType, a.FileName, a.Content))
                    .ToList();
                var input = new CliTurnInputMessage(message.MessageId, message.Text, attachments);
                var sent = _record is null
                    ? await _input.SendUserMessageAsync(input, cancellationToken)
                    : await SendRecordedAsync(_record, input, cancellationToken);
                if (!sent)
                {
                    break;
                }

                written.Add(message.MessageId);
                _logger.LogInformation(
                    "CLI run {RunId}: handed queued message {MessageId} of thread {ThreadId} to the running turn", _runId, message.MessageId, _threadId);
            }
        }
        finally
        {
            var untaken = claimed.Select(m => m.MessageId).Except(written).ToList();
            if (untaken.Count > 0)
            {
                // 进程此刻不收（这一轮正在收尾、或停在两轮之间）：放回等待，下一次再交，或者随这一轮结束作为新的一轮发出
                await delivery.ReturnUntakenAsync(untaken, CancellationToken.None);
            }
        }

        return written;
    }
    /// <summary>
    /// 主循环每处理完一条事件调用（持有序号闸门）：停机前写进这个进程、记在此刻序号上（或更早）的消息，原样再交给适配器一次。
    /// </summary>
    /// <remarks>与批准通道的重放同理，必须紧跟在那条事件之后做：依赖这一行 stdin 的输出行要等它重新发出才会交出来。</remarks>
    public async Task ReplayRecordedAsync()
    {
        if (_record is null || !_record.HasPendingReplay)
        {
            return;
        }

        while (_record.NextReplay() is { } entry)
        {
            var content = await LoadReplayContentAsync(_record);
            var message = content.TryGetValue(entry.MessageId, out var handed)
                ? new CliTurnInputMessage(
                    handed.MessageId, handed.Text,
                    handed.Attachments.Select(a => new CliPromptAttachment(a.MediaType, a.FileName, a.Content)).ToList())
                // 那一行已经不在队列里（线程删了）：进程收到过它，行数照样要对上
                : new CliTurnInputMessage(entry.MessageId, string.Empty, []);

            if (!await _input.SendUserMessageAsync(message, CancellationToken.None))
            {
                _logger.LogWarning(
                    "CLI run {RunId}: replaying its reattached process, the adapter would not take queued message {MessageId} again " +
                    "where it was handed over before the restart; the replay may be out of step from here", _runId, entry.MessageId);
            }
        }
    }

    /// <summary>
    /// 写进一个可重新附着的进程：在序号闸门里先记下（写的这一刻的序号），再写；没写进去就撤掉记录。重放还没追上时不写。
    /// </summary>
    private async Task<bool> SendRecordedAsync(CliQueuedInputRecord record, CliTurnInputMessage message, CancellationToken cancellationToken)
    {
        var sequence = record.Sequence;
        await sequence.Gate.WaitAsync(cancellationToken);
        try
        {
            if (!record.MayWrite())
            {
                return false;
            }

            var entry = new CliHandedInput(sequence.Current, message.Id);
            var recorded = record.With(entry);
            try
            {
                await PersistAsync(recorded);
            }
            catch
            {
                record.Without(entry);
                throw;
            }

            var sent = false;
            try
            {
                sent = await _input.SendUserMessageAsync(message, cancellationToken);
            }
            finally
            {
                if (!sent)
                {
                    await PersistAsync(record.Without(entry));
                }
            }

            return sent;
        }
        finally
        {
            sequence.Gate.Release();
        }
    }

    private async Task PersistAsync(string? handedInputs)
    {
        using var scope = _scopeFactory.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IRepository<CliRun, Guid>>().AsQueryable()
            .IgnoreQueryFilters()
            .Where(r => r.Id == _runId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.HandedInputs, handedInputs), CancellationToken.None);
    }

    private async Task<IReadOnlyDictionary<Guid, QueuedHandover>> LoadReplayContentAsync(CliQueuedInputRecord record)
    {
        if (record.ReplayContent is { } loaded)
        {
            return loaded;
        }

        IReadOnlyList<QueuedHandover> handed = [];
        try
        {
            using var scope = _scopeFactory.CreateScope();
            if (scope.ServiceProvider.GetService<IChatQueueDelivery>() is { } delivery)
            {
                handed = await delivery.GetHandedOverAsync(_runId, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            // 内容只影响之后事件里显示的正文：行数与次序照样对得上
            _logger.LogWarning(ex, "CLI run {RunId}: could not load the queued messages handed to it before the restart", _runId);
        }

        record.ReplayContent = handed.ToDictionary(m => m.MessageId);
        return record.ReplayContent;
    }
}

/// <summary>写进一个可重新附着进程的一条排队消息：写它那一刻最后一个已分配的事件序号，与消息 id。</summary>
internal readonly record struct CliHandedInput(int Sequence, Guid MessageId);

/// <summary>
/// 一个可重新附着进程上写进去的排队消息（<see cref="CliRun.HandedInputs"/>）：这一次尝试与之前接过同一个进程的尝试写的都在里面，
/// 之前写的在重放中按序号再交一次。
/// </summary>
internal sealed class CliQueuedInputRecord
{
    private readonly List<CliHandedInput> _entries;
    private readonly Queue<CliHandedInput> _replay;
    private readonly int _attemptStart;
    private readonly int _persistedThrough;
    private readonly Func<bool> _stdinCaughtUp;

    /// <param name="sequence">与主循环共用的序号（构造时的值是这一次尝试的起点）。</param>
    /// <param name="recorded">停机前记下的（<see cref="CliRun.HandedInputs"/>）；新进程为 null。</param>
    /// <param name="persistedThrough">重放时已落库的最后一个事件序号；新进程为 <see cref="int.MinValue"/>。</param>
    /// <param name="stdinCaughtUp">重放已经重新发出了进程收到过的全部 stdin 行；新进程恒为 true。</param>
    public CliQueuedInputRecord(CliEventSequence sequence, string? recorded, int persistedThrough, Func<bool> stdinCaughtUp)
    {
        Sequence = Check.NotNull(sequence);
        _attemptStart = sequence.Current;
        _entries = Parse(recorded);
        _replay = new Queue<CliHandedInput>(_entries);
        _persistedThrough = persistedThrough;
        _stdinCaughtUp = Check.NotNull(stdinCaughtUp);
    }

    public CliEventSequence Sequence { get; }

    /// <summary>重放要交的消息的内容（第一次用到时从队列读）。</summary>
    public IReadOnlyDictionary<Guid, QueuedHandover>? ReplayContent { get; set; }

    public bool HasPendingReplay => _replay.Count > 0;

    /// <summary>下一条记在此刻序号上（或更早）、还没再交过的消息；没有时为 null。持有序号闸门时调用。</summary>
    public CliHandedInput? NextReplay()
        => _replay.TryPeek(out var next) && next.Sequence <= Sequence.Current ? _replay.Dequeue() : null;

    /// <summary>
    /// 此刻能不能写一条新的（持有序号闸门时调用）：这一次尝试已经有了事件（提示已写出，重放时的位置才有着落）、
    /// 重放追上了停机前落库的事件、之前写的都已再交过、进程收到过的 stdin 都已重新发出。
    /// </summary>
    public bool MayWrite()
        => Sequence.Current > _attemptStart
           && Sequence.Current >= _persistedThrough
           && _replay.Count == 0
           && _stdinCaughtUp();

    /// <summary>加上一条，返回要落库的整列。</summary>
    public string? With(CliHandedInput entry)
    {
        _entries.Add(entry);
        return Format(_entries);
    }

    /// <summary>撤掉一条（没写进去），返回要落库的整列。</summary>
    public string? Without(CliHandedInput entry)
    {
        _entries.Remove(entry);
        return Format(_entries);
    }

    internal static List<CliHandedInput> Parse(string? recorded)
    {
        var entries = new List<CliHandedInput>();
        if (string.IsNullOrWhiteSpace(recorded))
        {
            return entries;
        }

        foreach (var line in recorded.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split(' ', 2);
            if (parts.Length == 2
                && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var sequence)
                && Guid.TryParse(parts[1], out var messageId))
            {
                entries.Add(new CliHandedInput(sequence, messageId));
            }
        }

        return entries;
    }

    internal static string? Format(IReadOnlyCollection<CliHandedInput> entries)
        => entries.Count == 0
            ? null
            : string.Concat(entries.Select(e => string.Create(CultureInfo.InvariantCulture, $"{e.Sequence} {e.MessageId}\n")));
}
