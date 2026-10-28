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
        Input = message.InputJson is null
            ? null
            : JsonSerializer.Deserialize<Dictionary<string, object?>>(message.InputJson, TnziJsonDefaults.Options),
        Output = message.Output,
        OutputBlocks = DeserializeBlocks(message.OutputBlocksJson),
        Status = message.Status,
        Level = message.Level,
        Permission = DeserializePermission(message.PermissionJson),
        Sequence = message.Sequence,
        CreatedAt = message.CreationTime
    };

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
