namespace Tnzi.AI.Services;

/// <summary>
/// 排队消息的附件 id 怎么存，以及一条排队消息能不能交给正在跑的那一轮：<see cref="ChatQueueService"/>（列出时预计「立即发送」会做什么）
/// 与 <see cref="ChatQueueDelivery"/>（真的交出）共用同一个判断。
/// </summary>
internal static class ChatQueueHandover
{
    /// <summary><see cref="QueuedThreadMessage.AttachmentIds"/> → id 列表（按附上的先后）；没有或读不出时为空。</summary>
    public static List<Guid> ParseIds(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<Guid>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>id 列表 → <see cref="QueuedThreadMessage.AttachmentIds"/>；空列表为 null。</summary>
    public static string? SerializeIds(IReadOnlyCollection<Guid> ids) => ids.Count == 0 ? null : JsonSerializer.Serialize(ids);

    /// <summary>
    /// 这条消息能不能交给运行 <paramref name="runId"/>：带文件的不能（运行中途没有放文件的地方）；其余按默认判断
    /// （带着 <c>StrictestValues</c> 里的键的不交，选了与这一轮不同的推理强度的不交），注册了 <see cref="IChatQueueHandoverPolicy"/> 时由它们全部同意才交。
    /// </summary>
    /// <param name="message">排队消息。</param>
    /// <param name="hasFiles">它带着不是图片或 PDF 的附件。</param>
    /// <param name="runId">要交给的那一轮。</param>
    /// <param name="runEffort">那一轮用的推理强度；null = 没给。</param>
    /// <param name="options">队列选项（<c>StrictestValues</c>）。</param>
    /// <param name="policies">应用的判断。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public static async Task<bool> CanGoMidTurnAsync(
        QueuedThreadMessage message, bool hasFiles, Guid runId, string? runEffort, ChatQueueOptions options,
        IEnumerable<IChatQueueHandoverPolicy> policies, CancellationToken cancellationToken)
    {
        if (hasFiles)
        {
            return false;
        }

        var metadata = ChatTurnMetadata.Parse(message.MetadataJson);
        var carriesTurnRules = metadata is not null && metadata.Keys.Any(options.StrictestValues.ContainsKey);
        // 强度钉在这一轮的进程上：选了别的强度的消息交进去会按这一轮的强度回答，所以等这一轮结束、自己作为一轮发出
        var otherEffort = !string.IsNullOrWhiteSpace(message.Effort)
                          && !string.Equals(message.Effort.Trim(), runEffort?.Trim(), StringComparison.OrdinalIgnoreCase);
        var request = new ChatQueueHandoverRequest(
            message.ThreadId, message.UserId, message.AgentId, runId, message.Id, message.Text, metadata, message.Model,
            DefaultDecision: !carriesTurnRules && !otherEffort)
        {
            Effort = string.IsNullOrWhiteSpace(message.Effort) ? null : message.Effort.Trim(),
            RunEffort = string.IsNullOrWhiteSpace(runEffort) ? null : runEffort.Trim()
        };

        var asked = false;
        foreach (var policy in policies)
        {
            asked = true;
            if (!await policy.CanHandOverAsync(request, cancellationToken))
            {
                return false;
            }
        }

        return asked || request.DefaultDecision;
    }

    /// <summary>附件的媒体类型里有没有文件（不是随提示交给模型的图片与 PDF）。</summary>
    public static bool HasFiles(IEnumerable<string> mediaTypes) => mediaTypes.Any(t => !ChatAttachmentRules.IsInlineMediaType(t));

    /// <summary>只有附件、没有正文的消息写进线程时用的文字：附件的名字（与聊天输入框发只有附件的一轮时一样）。</summary>
    public static string TextOrNames(string text, IEnumerable<(string MediaType, string? FileName)> attachments)
        => !string.IsNullOrWhiteSpace(text)
            ? text
            : string.Join(", ", attachments.Select(a => string.IsNullOrWhiteSpace(a.FileName) ? a.MediaType : a.FileName));
}
