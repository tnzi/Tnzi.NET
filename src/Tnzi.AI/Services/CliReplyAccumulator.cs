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
/// </remarks>
public sealed class CliReplyAccumulator
{
    /// <summary>文本块之间的分隔。</summary>
    public const string BlockSeparator = "\n\n";

    private readonly StringBuilder _text = new();
    private readonly List<ToolCallDetail> _calls = [];
    private readonly Dictionary<string, (ToolCallDetail Call, DateTime? StartedAt)> _byId = new(StringComparer.Ordinal);
    private bool _lastWasText;

    /// <summary>到目前为止的回复文本。</summary>
    public string Text => _text.ToString();

    /// <summary>到目前为止的工具调用（按发起顺序，每个调用一条）。返回的是副本。</summary>
    public IReadOnlyList<ToolCallDetail> ToolCalls => _calls.Select(c => c.Copy()).ToList();

    /// <summary>是否有任何用户看得见的内容（文本或工具调用）。</summary>
    public bool HasContent => _text.Length > 0 || _calls.Count > 0;

    /// <summary>
    /// 吸收一条事件，返回它对用户可见状态的改动：追加的文本（含块分隔），或改动后的那条工具调用（副本）。
    /// 与回复无关的事件（状态、日志、错误）返回空改动。
    /// </summary>
    public CliReplyUpdate Apply(CliAgentEvent evt)
    {
        Check.NotNull(evt);

        switch (evt.Type)
        {
            case CliAgentEventType.Text:
                return new CliReplyUpdate(AppendText(evt.Content), null);

            case CliAgentEventType.Thinking:
                // 思考不进回复，但它把前后两段文本隔成两个块
                _lastWasText = false;
                return CliReplyUpdate.None;

            case CliAgentEventType.ToolUse:
                _lastWasText = false;
                return new CliReplyUpdate(null, StartCall(evt));

            case CliAgentEventType.ToolResult:
                _lastWasText = false;
                return new CliReplyUpdate(null, FinishCall(evt));

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

        var appended = !_lastWasText && _text.Length > 0 ? BlockSeparator + content : content;
        _text.Append(appended);
        _lastWasText = true;
        return appended;
    }

    private ToolCallDetail StartCall(CliAgentEvent evt)
    {
        var input = ToJson(evt.Input);

        if (!string.IsNullOrEmpty(evt.CallId) && _byId.TryGetValue(evt.CallId, out var existing))
        {
            // 同一调用的第二条发起事件（ACP 先报 pending 再报参数）：补全信息，不另起一条
            existing.Call.Name = string.IsNullOrEmpty(evt.Tool) ? existing.Call.Name : evt.Tool;
            existing.Call.Input = input ?? existing.Call.Input;
            return existing.Call.Copy();
        }

        var call = new ToolCallDetail
        {
            Id = evt.CallId,
            Name = evt.Tool ?? string.Empty,
            Input = input,
            Status = ToolCallStatuses.Running,
            TextOffset = _text.Length
        };

        _calls.Add(call);
        if (!string.IsNullOrEmpty(evt.CallId))
        {
            _byId[evt.CallId] = (call, evt.CreatedAt);
        }

        return call.Copy();
    }

    private ToolCallDetail FinishCall(CliAgentEvent evt)
    {
        ToolCallDetail call;
        DateTime? startedAt = null;
        if (!string.IsNullOrEmpty(evt.CallId) && _byId.TryGetValue(evt.CallId, out var existing))
        {
            call = existing.Call;
            startedAt = existing.StartedAt;
        }
        else
        {
            // 没见过发起事件的结果（中途接入、发起事件丢了）：照样成条，名字取结果自带的
            call = new ToolCallDetail { Id = evt.CallId, TextOffset = _text.Length };
            _calls.Add(call);
            if (!string.IsNullOrEmpty(evt.CallId))
            {
                _byId[evt.CallId] = (call, null);
            }
        }

        if (string.IsNullOrEmpty(call.Name) && !string.IsNullOrEmpty(evt.Tool))
        {
            call.Name = evt.Tool;
        }

        var failed = string.Equals(evt.Status, ToolCallStatuses.Failed, StringComparison.OrdinalIgnoreCase);
        call.Status = failed ? ToolCallStatuses.Failed : ToolCallStatuses.Completed;
        call.IsSuccess = !failed;
        call.Result = ToBlocks(evt);
        call.Error = failed ? FirstText(call.Result) : null;

        if (startedAt is { } start && evt.CreatedAt is { } end && end >= start)
        {
            call.DurationMs = (end - start).TotalMilliseconds;
        }

        return call.Copy();
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
}
