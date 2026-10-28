namespace Tnzi.AI.Engine;

/// <summary>
/// 流式用量的累计口径：<see cref="AgentStreamChunk.Usage"/> 是一次（或若干次）LLM 调用的用量<b>增量</b>，
/// 一次运行会出现多个，消费方必须逐个相加，不能取最后一个。
/// </summary>
/// <remarks>
/// 执行器每完成一次 LLM 调用就发出那一次的用量（工具循环的中间轮在执行工具之前单独发一个只带用量的 chunk，
/// 最后一轮随终止 chunk 发出）；多 Agent 策略把每个参与者的调用都原样转发。所以「流里所有 Usage 之和」
/// 就是这次运行实际计费的量，与非流式路径的 <see cref="AgentRunResult.Usage"/> 同一口径。
/// 只带用量、没有任何面向客户端载荷的 chunk 用 <see cref="IsUsageOnly"/> 识别，向外转发事件的消费方据此跳过它。
/// </remarks>
public static class StreamUsage
{
    /// <summary>
    /// 两份用量相加，返回新对象（不修改入参）；两者都为 null 时返回 null。
    /// <see cref="TokenUsageDto.TotalTokens"/> 取相加后的输入加输出，不累加各增量自报的总数
    /// （部分 provider 不报总数，累加会让总数与分项对不上）。
    /// </summary>
    public static TokenUsageDto? Add(TokenUsageDto? total, TokenUsageDto? delta)
    {
        if (delta == null)
        {
            return total;
        }

        var input = (total?.InputTokens ?? 0) + delta.InputTokens;
        var output = (total?.OutputTokens ?? 0) + delta.OutputTokens;
        return new TokenUsageDto
        {
            InputTokens = input,
            OutputTokens = output,
            TotalTokens = input + output,
            CachedInputTokens = (total?.CachedInputTokens ?? 0) + delta.CachedInputTokens,
            CacheCreationTokens = (total?.CacheCreationTokens ?? 0) + delta.CacheCreationTokens
        };
    }

    /// <summary>
    /// 一串 chunk 的用量之和；没有任何 chunk 带用量时返回 null。
    /// </summary>
    public static TokenUsageDto? Sum(IEnumerable<AgentStreamChunk> chunks)
    {
        Check.NotNull(chunks);

        TokenUsageDto? total = null;
        foreach (var chunk in chunks)
        {
            total = Add(total, chunk.Usage);
        }
        return total;
    }

    /// <summary>
    /// chunk 是否只携带用量增量，没有任何面向客户端的载荷（文本、工具调用、事件、结束原因等）。
    /// </summary>
    public static bool IsUsageOnly(AgentStreamChunk chunk)
    {
        Check.NotNull(chunk);

        return chunk.Usage != null
            && chunk.FinishReason == null
            && chunk.Citations == null
            && chunk.UserMessageId == null
            && chunk.AssistantMessageId == null
            && !AgentStreamMapper.HasClientPayload(chunk);
    }
}
