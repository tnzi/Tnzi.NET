namespace Tnzi.AI.Cli.Dispatch;

/// <summary>
/// 落库的事件行 ⇄ 归一化事件。
/// </summary>
/// <remarks>
/// 三个读者共用它：调度器的实时流（客户端看到的）、管理端的事件列表，以及执行器在运行结束时把事件流折叠成写进线程的回复。
/// 三处读出来的必须是同一个东西，否则刷新之后的工具卡片与流式时看到的对不上。
/// </remarks>
internal static class CliRunMessageConverter
{
    /// <summary>一条事件行 → 归一化事件（带序号与落库时间）。</summary>
    public static CliAgentEvent ToEvent(CliRunMessage message) => new()
    {
        Type = message.Type,
        Content = message.Content,
        Tool = message.Tool,
        CallId = message.CallId,
        ParentCallId = message.ParentCallId,
        Input = message.InputJson is null || HasStructuredPayload(message.Type)
            ? null
            : JsonSerializer.Deserialize<Dictionary<string, object?>>(message.InputJson, TnziJsonDefaults.Options),
        Output = message.Output,
        OutputBlocks = DeserializeBlocks(message.OutputBlocksJson),
        Status = message.Status,
        Level = message.Level,
        Permission = DeserializePermission(message.PermissionJson),
        Turn = message.Type == CliAgentEventType.TurnCompleted ? DeserializeOrNull<CliTurnBoundary>(message.InputJson) : null,
        Compaction = message.Type == CliAgentEventType.Compacted ? DeserializeOrNull<CliCompaction>(message.InputJson) : null,
        Task = message.Type == CliAgentEventType.Task ? DeserializeOrNull<CliTask>(message.InputJson) : null,
        UserMessage = message.Type == CliAgentEventType.UserMessage ? DeserializeOrNull<CliUserMessage>(message.InputJson) : null,
        Sequence = message.Sequence,
        CreatedAt = message.CreationTime
    };

    /// <summary>
    /// 事件行 <c>InputJson</c> 列里存什么：工具入参，或（一轮收尾、上下文压缩、任务、交给这一轮的用户消息）事件自己的结构化载荷。
    /// 两类事件从不带工具入参，共用一列免得为两个小对象各加一列。
    /// </summary>
    public static string? SerializePayload(CliAgentEvent evt) => evt.Type switch
    {
        CliAgentEventType.TurnCompleted when evt.Turn is not null => JsonSerializer.Serialize(evt.Turn, TnziJsonDefaults.Options),
        CliAgentEventType.Compacted when evt.Compaction is not null => JsonSerializer.Serialize(evt.Compaction, TnziJsonDefaults.Options),
        CliAgentEventType.Task when evt.Task is not null => JsonSerializer.Serialize(evt.Task, TnziJsonDefaults.Options),
        CliAgentEventType.UserMessage when evt.UserMessage is not null => JsonSerializer.Serialize(evt.UserMessage, TnziJsonDefaults.Options),
        _ => evt.Input is null ? null : JsonSerializer.Serialize(evt.Input, TnziJsonDefaults.Options)
    };

    private static bool HasStructuredPayload(CliAgentEventType type)
        => type is CliAgentEventType.TurnCompleted or CliAgentEventType.Compacted or CliAgentEventType.Task or CliAgentEventType.UserMessage;

    private static T? DeserializeOrNull<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, TnziJsonDefaults.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>落库的权限 JSON → 权限请求或结局；解析不了时返回 null。</summary>
    public static CliAgentPermission? DeserializePermission(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<CliAgentPermission>(json, TnziJsonDefaults.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>落库的问题回答 JSON（问题原文 → 回答列表）→ 回答；没有或解析不了时返回 null（按没有回答处理）。</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>>? DeserializeAnswers(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json, TnziJsonDefaults.Options)?
                .ToDictionary(p => p.Key, p => (IReadOnlyList<string>)p.Value, StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>落库的问题 JSON → 问题；没有或解析不了时返回空列表。</summary>
    public static IReadOnlyList<CliQuestion> DeserializeQuestions(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<CliQuestion>>(json, TnziJsonDefaults.Options) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>工具输出分块 → 落库的 JSON（只存文本与附件 id，不存图片数据）。</summary>
    public static string? SerializeBlocks(IReadOnlyList<CliContentBlock>? blocks)
    {
        if (blocks is not { Count: > 0 })
        {
            return null;
        }

        var stored = blocks.Select(b => b with { Data = null }).ToList();
        return JsonSerializer.Serialize(stored, TnziJsonDefaults.Options);
    }

    /// <summary>落库的 JSON → 工具输出分块；解析不了时返回 null（只剩文本形态的 Output）。</summary>
    public static IReadOnlyList<CliContentBlock>? DeserializeBlocks(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<List<CliContentBlock>>(json, TnziJsonDefaults.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
