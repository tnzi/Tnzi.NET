namespace Tnzi.AI.Middleware;

/// <summary>
/// 本轮用户输入「模型实际收到的是什么」的唯一口径。
/// </summary>
/// <remarks>
/// <para>
/// 一次请求可以同时带 <see cref="AgentRunRequest.UserMessage"/> 与 <see cref="AgentRunRequest.ContentParts"/>，
/// 而 <c>IAgentResolver.BuildChatMessageAsync</c> 在内容块非空时只读内容块、完全不看 Message。
/// 守卫、历史落库、上下文检索若各自读 Message，评估与记录的就不是模型收到的那段文本：
/// Message 填一句无害的话、恶意文本放进内容块，守卫全部放行而模型照单全收。
/// 这几处一律经本类取文本，与 AgentRuntime 构建用户消息同源。
/// </para>
/// <para>
/// 守卫改写（脱敏等）的结果落在 <see cref="AiMiddlewareContext.EffectiveUserMessage"/> /
/// <see cref="AiMiddlewareContext.EffectiveContentParts"/> 上，原请求不动。
/// </para>
/// </remarks>
internal static class UserTurn
{
    private const string PartSeparator = "\n";

    /// <summary>本轮模型收到的内容块：守卫改写过的副本优先，其次请求原值。</summary>
    internal static List<ContentPartDto>? EffectiveParts(AiMiddlewareContext context)
        => context.EffectiveContentParts ?? context.Request.ContentParts;

    /// <summary>本轮模型收到的 Message：守卫改写过的优先，其次请求原值（内容块非空时模型不读它）。</summary>
    internal static string? EffectiveMessage(AiMiddlewareContext context)
        => context.EffectiveUserMessage ?? context.Request.UserMessage;

    /// <summary>本轮模型收到的用户文本（守卫改写之后）。没有用户文本时返回空串。</summary>
    internal static string EffectiveText(AiMiddlewareContext context)
        => TextOf(EffectiveMessage(context), EffectiveParts(context));

    /// <summary>本轮是否有用户输入要交给模型（文本或任意内容块）。</summary>
    internal static bool IsPresent(AiMiddlewareContext context)
        => EffectiveParts(context) is { Count: > 0 } || !string.IsNullOrWhiteSpace(EffectiveMessage(context));

    /// <summary>
    /// 模型会收到的用户文本：内容块非空时是其中全部文本块（按出现顺序换行拼接），否则是 Message。
    /// </summary>
    internal static string TextOf(string? message, IReadOnlyList<ContentPartDto>? parts)
    {
        if (parts is not { Count: > 0 })
        {
            return message ?? string.Empty;
        }

        return string.Join(PartSeparator, TextPartsOf(parts).Select(p => p.Text));
    }

    /// <summary>
    /// 用守卫改写后的文本替换内容块里的文本，返回新列表（原列表与其中的块都不修改）。
    /// </summary>
    /// <remarks>
    /// 只有一个文本块时原位替换。多个文本块时改写结果是对拼接文本整体算出来的，无法逐块对回去，
    /// 于是把所有文本块合并成一个、放在第一个文本块的位置，图片与文件块保持原顺序。
    /// 模型收到的文本与守卫放行的文本逐字一致，代价只是文本块被合并。
    /// </remarks>
    internal static List<ContentPartDto> WithText(IReadOnlyList<ContentPartDto> parts, string text)
    {
        Check.NotNull(parts);
        Check.NotNull(text);

        var result = new List<ContentPartDto>(parts.Count + 1);
        var textPlaced = false;
        foreach (var part in parts)
        {
            if (part is not TextContentPartDto textPart || string.IsNullOrWhiteSpace(textPart.Text))
            {
                result.Add(part);
                continue;
            }

            if (!textPlaced)
            {
                result.Add(new TextContentPartDto { Text = text });
                textPlaced = true;
            }
        }

        if (!textPlaced && !string.IsNullOrEmpty(text))
        {
            result.Insert(0, new TextContentPartDto { Text = text });
        }

        return result;
    }

    // 与 BuildChatMessageAsync 一致：空白文本块不进模型
    private static IEnumerable<TextContentPartDto> TextPartsOf(IReadOnlyList<ContentPartDto> parts)
        => parts.OfType<TextContentPartDto>().Where(p => !string.IsNullOrWhiteSpace(p.Text));
}
