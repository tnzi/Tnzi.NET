namespace Tnzi.AI.Services;

/// <summary>
/// 把一次外部运行的事件流折叠成「用户看到的回复」：回复文本与按调用合并的工具调用。
/// </summary>
/// <remarks>
/// <para>
/// 同一份逻辑有两个使用者，用同一个类是为了它们不可能各说各话：路由门面边收事件边用它产出流式块，
/// <c>Tnzi.AI.Cli</c> 在运行结束时用它把整条事件流折叠成写进线程的那条回复。于是刷新之后看到的回复与工具卡片，
/// 逐字就是流式时看到的那些（包括工具卡片夹在第几个字符处，见 <see cref="ToolCallDetail.TextOffset"/>）。
/// </para>
/// <para>
/// 文本：stream-json 的每条文本事件是一个完整的文本块，ACP 的是一个块的片段。块与块之间（中间隔着工具调用或思考）
/// 插一个空行，同一块的片段直接相连。判据是「上一条有意义的事件是不是文本」，状态与日志事件不算。
/// </para>
/// <para>
/// 工具调用：按调用 id 合并。发起时一条（<c>running</c>，带入参），结果到达时更新同一条（<c>completed</c> /
/// <c>failed</c>，带结果块与耗时）。没有 id 的调用各自成条；只有结果、找不到发起的那一条照样成条，不丢。
/// </para>
/// <para>
/// 子 agent：带 <see cref="CliAgentEvent.ParentCallId"/> 的事件是子 agent 做的。它写的字不进回复文本，记成派出它的那次调用
/// （<see cref="ToolCallDetail.Task"/>）的一步；它调的工具照样成条，但带 <see cref="ToolCallDetail.ParentId"/>、不占回复里的位置；
/// 它的思考不进任何地方。<see cref="CliAgentEventType.Task"/> 事件更新起任务的那次调用上的任务（子 agent、后台命令）状态（<see cref="ToolCallDetail.Task"/>）。
/// </para>
/// <para>
/// 轮次：运行中途结束的一轮（<see cref="CliAgentEventType.TurnCompleted"/>）各自写成一条消息，<see cref="BeginTurn"/> 之后的
/// <see cref="Text"/> / <see cref="ToolCalls"/> 是新一轮的。后台任务跨轮：它在后面几轮里的进展与结局仍记在起它的那一轮，
/// <see cref="EarlierTurnsChanged"/> 说哪几轮因此变了，写消息的一方据此重写那几条消息的工具调用。
/// </para>
/// </remarks>
public sealed class CliReplyAccumulator
{
    /// <summary>文本块之间的分隔。</summary>
    public const string BlockSeparator = "\n\n";

    private readonly List<Turn> _turns = [new()];
    private readonly Dictionary<string, Entry> _byId = new(StringComparer.Ordinal);
    private readonly SortedSet<int> _changedEarlierTurns = [];

    private Turn Current => _turns[^1];

    private int CurrentIndex => _turns.Count - 1;

    /// <summary>到目前为止这一轮的回复文本。</summary>
    public string Text => Current.Text.ToString();

    /// <summary>到目前为止这一轮的工具调用（按发起顺序，每个调用一条，子 agent 的调用带 <see cref="ToolCallDetail.ParentId"/>）。返回的是副本。</summary>
    public IReadOnlyList<ToolCallDetail> ToolCalls => ToolCallsOfTurn(CurrentIndex);

    /// <summary>这一轮是否有任何用户看得见的内容（文本或工具调用）。</summary>
    public bool HasContent => Current.Text.Length > 0 || Current.Calls.Count > 0;

    /// <summary>当前这一轮的序号（从 0 起，每个 <see cref="BeginTurn"/> 加一）。</summary>
    public int TurnIndex => CurrentIndex;

    /// <summary>当前这一轮之前、因为后来的事件（后台任务的进展与结局）改动过工具调用的那几轮的序号。</summary>
    public IReadOnlyList<int> EarlierTurnsChanged => _changedEarlierTurns.ToList();

    /// <summary>某一轮的工具调用（副本）。</summary>
    public IReadOnlyList<ToolCallDetail> ToolCallsOfTurn(int turn)
        => turn >= 0 && turn < _turns.Count ? _turns[turn].Calls.Select(c => c.Copy()).ToList() : [];

    /// <summary>开始新的一轮：之后的文本与工具调用属于它。前几轮起的任务后来的事件仍记在它们自己那一轮。</summary>
    public void BeginTurn() => _turns.Add(new Turn());

    /// <summary>
    /// 还在跑的任务一律以 <paramref name="status"/> 结束（进程结束时它们随之停止）。返回改动过的调用（副本）。
    /// </summary>
    public IReadOnlyList<ToolCallDetail> SettleTasks(string status = CliTaskStatuses.Stopped)
    {
        var settled = new List<ToolCallDetail>();
        for (var i = 0; i < _turns.Count; i++)
        {
            foreach (var call in _turns[i].Calls.Where(c => c.Task is { Status: CliTaskStatuses.Running }))
            {
                call.Task!.Status = status;
                MarkChanged(i);
                settled.Add(call.Copy());
            }
        }

        return settled;
    }

    /// <summary>
    /// 吸收一条事件，返回它对用户可见状态的改动：追加的文本（含块分隔），或改动后的工具调用（副本）。
    /// 与回复无关的事件（状态、日志、错误）返回空改动。
    /// </summary>
    public CliReplyUpdate Apply(CliAgentEvent evt)
    {
        Check.NotNull(evt);

        var inSubAgent = !string.IsNullOrEmpty(evt.ParentCallId);
        switch (evt.Type)
        {
            case CliAgentEventType.Text when inSubAgent:
                return SubAgentText(evt);

            case CliAgentEventType.Text:
                return new CliReplyUpdate(AppendText(evt.Content), null);

            case CliAgentEventType.Thinking:
                // 思考不进回复，但它把前后两段文本隔成两个块；子 agent 的思考与主回复无关
                if (!inSubAgent)
                {
                    Current.LastWasText = false;
                }

                return CliReplyUpdate.None;

            case CliAgentEventType.ToolUse:
                if (!inSubAgent)
                {
                    Current.LastWasText = false;
                }

                return StartCall(evt);

            case CliAgentEventType.ToolResult:
                if (!inSubAgent)
                {
                    Current.LastWasText = false;
                }

                return FinishCall(evt);

            case CliAgentEventType.Task:
                return UpdateTask(evt);

            default:
                return CliReplyUpdate.None;
        }
    }

    private string? AppendText(string? content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return null;
        }

        var turn = Current;
        var appended = !turn.LastWasText && turn.Text.Length > 0 ? BlockSeparator + content : content;
        turn.Text.Append(appended);
        turn.LastWasText = true;
        return appended;
    }

    private CliReplyUpdate StartCall(CliAgentEvent evt)
    {
        var input = ToJson(evt.Input);

        if (!string.IsNullOrEmpty(evt.CallId) && _byId.TryGetValue(evt.CallId, out var existing))
        {
            // 同一调用的第二条发起事件（ACP 先报 pending 再报参数），或子 agent 事件先占了位的派出调用：补全信息，不另起一条
            existing.Call.Name = string.IsNullOrEmpty(evt.Tool) ? existing.Call.Name : evt.Tool;
            existing.Call.Input = input ?? existing.Call.Input;
            existing.StartedAt ??= evt.CreatedAt;
            MarkChanged(existing.Turn);
            return Changed(existing.Call);
        }

        var parent = string.IsNullOrEmpty(evt.ParentCallId) ? null : Dispatch(evt.ParentCallId, null, evt.CreatedAt);
        var call = new ToolCallDetail
        {
            Id = evt.CallId,
            Name = evt.Tool ?? string.Empty,
            Input = input,
            Status = ToolCallStatuses.Running,
            ParentId = parent?.Call.Id,
            // 子 agent 的调用不在回复文本里，没有位置
            TextOffset = parent is null ? Current.Text.Length : null
        };

        var entry = Add(call, evt.CreatedAt, parent?.Turn ?? CurrentIndex);
        if (parent is null)
        {
            return Changed(call);
        }

        TaskOf(parent).Steps.Add(new ToolTaskStep { Type = ToolTaskStep.ToolType, ToolCallId = call.Id });
        MarkChanged(parent.Turn);
        return Changed(entry.Call, parent.Call);
    }

    private CliReplyUpdate FinishCall(CliAgentEvent evt)
    {
        Entry entry;
        Entry? parent = null;
        if (!string.IsNullOrEmpty(evt.CallId) && _byId.TryGetValue(evt.CallId, out var existing))
        {
            entry = existing;
        }
        else
        {
            // 没见过发起事件的结果（中途接入、发起事件丢了）：照样成条，名字取结果自带的
            parent = string.IsNullOrEmpty(evt.ParentCallId) ? null : Dispatch(evt.ParentCallId, null, evt.CreatedAt);
            entry = Add(
                new ToolCallDetail
                {
                    Id = evt.CallId,
                    ParentId = parent?.Call.Id,
                    TextOffset = parent is null ? Current.Text.Length : null
                },
                null,
                parent?.Turn ?? CurrentIndex);
            if (parent is not null)
            {
                TaskOf(parent).Steps.Add(new ToolTaskStep { Type = ToolTaskStep.ToolType, ToolCallId = evt.CallId });
            }
        }

        var call = entry.Call;
        if (string.IsNullOrEmpty(call.Name) && !string.IsNullOrEmpty(evt.Tool))
        {
            call.Name = evt.Tool;
        }

        var failed = string.Equals(evt.Status, ToolCallStatuses.Failed, StringComparison.OrdinalIgnoreCase);
        call.Status = failed ? ToolCallStatuses.Failed : ToolCallStatuses.Completed;
        call.IsSuccess = !failed;
        call.Result = ToBlocks(evt);
        call.Error = failed ? FirstText(call.Result) : null;

        if (entry.StartedAt is { } start && evt.CreatedAt is { } end && end >= start)
        {
            call.DurationMs = (end - start).TotalMilliseconds;
        }

        // 前台任务随起它的调用返回而结束；结束通知没来（CLI 版本不发）时按调用的结局算。后台的调用当场返回，与任务的结局无关
        if (call.Task is { Background: false, Status: CliTaskStatuses.Running } task)
        {
            task.Status = failed ? CliTaskStatuses.Failed : CliTaskStatuses.Completed;
        }

        MarkChanged(entry.Turn);
        return parent is null ? Changed(call) : Changed(call, parent.Call);
    }

    private CliReplyUpdate SubAgentText(CliAgentEvent evt)
    {
        if (string.IsNullOrEmpty(evt.Content))
        {
            return CliReplyUpdate.None;
        }

        var parent = Dispatch(evt.ParentCallId!, null, evt.CreatedAt);
        var steps = TaskOf(parent).Steps;
        if (steps.Count > 0 && steps[^1].Type == ToolTaskStep.TextType)
        {
            steps[^1].Text += BlockSeparator + evt.Content;
        }
        else
        {
            steps.Add(new ToolTaskStep { Type = ToolTaskStep.TextType, Text = evt.Content });
        }

        MarkChanged(parent.Turn);
        return Changed(parent.Call);
    }

    private CliReplyUpdate UpdateTask(CliAgentEvent evt)
    {
        if (string.IsNullOrEmpty(evt.CallId) || evt.Task is not { } update)
        {
            return CliReplyUpdate.None;
        }

        var entry = Dispatch(evt.CallId, evt.ParentCallId, evt.CreatedAt);
        var task = TaskOf(entry);
        task.TaskId = update.TaskId ?? task.TaskId;
        task.Kind = update.Kind ?? task.Kind;
        task.AgentType = update.AgentType ?? task.AgentType;
        task.Model = update.Model ?? task.Model;
        task.Description = update.Description ?? task.Description;
        task.Prompt = update.Prompt ?? task.Prompt;
        task.Background = update.Background ?? task.Background;
        task.ToolUseCount = update.ToolUseCount ?? task.ToolUseCount;
        task.TotalTokens = update.TotalTokens ?? task.TotalTokens;
        task.DurationMs = update.DurationMs ?? task.DurationMs;
        task.Activity = update.Activity ?? task.Activity;
        task.Result = update.Summary ?? task.Result;
        task.StartedAt ??= evt.CreatedAt;

        // 没说状态的事件不改状态；结局不被晚到的进展改回进行中
        if (update.Status is { } status && !(CliTaskStatuses.IsFinished(task.Status) && status == CliTaskStatuses.Running))
        {
            task.Status = status;
        }

        MarkChanged(entry.Turn);
        return Changed(entry.Call);
    }

    /// <summary>
    /// 派出子 agent 的那次调用。还没见过它（子 agent 的事件先到，或它在交出进程之前的另一次运行里）时占一个位：
    /// 有上一层时挂在上一层下面，否则放在这一轮。
    /// </summary>
    private Entry Dispatch(string callId, string? parentCallId, DateTime? at)
    {
        if (_byId.TryGetValue(callId, out var existing))
        {
            if (existing.Call.ParentId is null && !string.IsNullOrEmpty(parentCallId) && parentCallId != callId)
            {
                existing.Call.ParentId = parentCallId;
            }

            return existing;
        }

        var parent = !string.IsNullOrEmpty(parentCallId) && parentCallId != callId ? Dispatch(parentCallId, null, at) : null;
        var call = new ToolCallDetail
        {
            Id = callId,
            Status = ToolCallStatuses.Running,
            ParentId = parent?.Call.Id,
            TextOffset = parent is null ? Current.Text.Length : null,
            Task = new ToolTaskDetail { StartedAt = at }
        };
        var entry = Add(call, at, parent?.Turn ?? CurrentIndex);
        if (parent is not null)
        {
            TaskOf(parent).Steps.Add(new ToolTaskStep { Type = ToolTaskStep.ToolType, ToolCallId = callId });
        }

        return entry;
    }

    private static ToolTaskDetail TaskOf(Entry entry)
        => entry.Call.Task ??= new ToolTaskDetail { StartedAt = entry.StartedAt };

    private Entry Add(ToolCallDetail call, DateTime? startedAt, int turn)
    {
        _turns[turn].Calls.Add(call);
        var entry = new Entry(call, startedAt, turn);
        if (!string.IsNullOrEmpty(call.Id))
        {
            _byId[call.Id] = entry;
        }

        MarkChanged(turn);
        return entry;
    }

    private void MarkChanged(int turn)
    {
        if (turn < CurrentIndex)
        {
            _changedEarlierTurns.Add(turn);
        }
    }

    private static CliReplyUpdate Changed(ToolCallDetail call, params ToolCallDetail[] related)
    {
        var copy = call.Copy();
        return new CliReplyUpdate(null, copy) { ToolCalls = [copy, .. related.Select(r => r.Copy())] };
    }

    private static List<ToolResultBlockDto> ToBlocks(CliAgentEvent evt)
    {
        if (evt.OutputBlocks is { Count: > 0 } blocks)
        {
            return blocks.Select(b => b.Type switch
                {
                    CliContentBlock.ImageType => new ToolResultBlockDto
                    {
                        Type = CliContentBlock.ImageType, MediaType = b.MediaType, AttachmentId = b.AttachmentId
                    },
                    CliContentBlock.FileType => new ToolResultBlockDto
                    {
                        Type = CliContentBlock.FileType, Name = b.Name, Uri = b.Uri, MediaType = b.MediaType
                    },
                    _ => new ToolResultBlockDto { Type = CliContentBlock.TextType, Text = b.Text ?? string.Empty }
                })
                .ToList();
        }

        return string.IsNullOrEmpty(evt.Output)
            ? []
            : [new ToolResultBlockDto { Type = CliContentBlock.TextType, Text = evt.Output }];
    }

    private static string? FirstText(List<ToolResultBlockDto>? blocks)
        => blocks?.FirstOrDefault(b => !string.IsNullOrEmpty(b.Text))?.Text;

    private static JsonElement? ToJson(IReadOnlyDictionary<string, object?>? input)
        => input is null ? null : JsonSerializer.SerializeToElement(input, TnziJsonDefaults.Options);

    private sealed class Turn
    {
        public StringBuilder Text { get; } = new();

        public List<ToolCallDetail> Calls { get; } = [];

        public bool LastWasText { get; set; }
    }

    private sealed class Entry(ToolCallDetail call, DateTime? startedAt, int turn)
    {
        public ToolCallDetail Call { get; } = call;

        public DateTime? StartedAt { get; set; } = startedAt;

        public int Turn { get; } = turn;
    }
}

/// <summary>
/// 一条事件对回复的改动。
/// </summary>
/// <param name="Text">追加的回复文本（含块分隔）；没有为 null。</param>
/// <param name="ToolCall">改动后的工具调用（副本）；没有为 null。</param>
public sealed record CliReplyUpdate(string? Text, ToolCallDetail? ToolCall)
{
    /// <summary>没有改动。</summary>
    public static CliReplyUpdate None { get; } = new(null, null);

    /// <summary>
    /// 这条事件改动的全部工具调用（副本），<see cref="ToolCall"/> 在第一个。子 agent 调一个工具时还有派出它的那次调用（多了一步）。
    /// </summary>
    public IReadOnlyList<ToolCallDetail> ToolCalls { get; init; } = ToolCall is null ? [] : [ToolCall];
}
