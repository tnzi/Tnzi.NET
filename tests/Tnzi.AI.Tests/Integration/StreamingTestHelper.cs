namespace Tnzi.AI.Tests.Integration;

/// <summary>
/// 流式响应测试辅助工具
/// </summary>
public static class StreamingTestHelper
{
    /// <summary>
    /// 收集异步流的所有 chunk 并拼接完整文本
    /// </summary>
    public static async Task<StreamingResult> CollectStreamAsync(
        IAsyncEnumerable<AgentStreamChunk> stream,
        CancellationToken cancellationToken = default)
    {
        var chunks = new List<AgentStreamChunk>();
        var textBuilder = new System.Text.StringBuilder();

        await foreach (var chunk in stream.WithCancellation(cancellationToken))
        {
            chunks.Add(chunk);
            if (!string.IsNullOrEmpty(chunk.Text))
            {
                textBuilder.Append(chunk.Text);
            }
        }

        return new StreamingResult
        {
            FullText = textBuilder.ToString(),
            AllChunks = chunks,
            FinishReason = chunks.LastOrDefault(c => c.FinishReason != null)?.FinishReason,
            Usage = chunks.LastOrDefault(c => c.Usage != null)?.Usage,
            ToolCallChunks = chunks.Where(c => c.IsToolCall).ToList(),
            ErrorChunks = chunks.Where(c => c.Error != null).ToList()
        };
    }
}

/// <summary>
/// 流式响应收集结果
/// </summary>
public class StreamingResult
{
    /// <summary>拼接后的完整文本</summary>
    public string FullText { get; init; } = string.Empty;

    /// <summary>所有收到的 chunk</summary>
    public List<AgentStreamChunk> AllChunks { get; init; } = [];

    /// <summary>最终完成原因</summary>
    public string? FinishReason { get; init; }

    /// <summary>Token 使用量（通常在最后一个 chunk）</summary>
    public TokenUsageDto? Usage { get; init; }

    /// <summary>工具调用相关的 chunk</summary>
    public List<AgentStreamChunk> ToolCallChunks { get; init; } = [];

    /// <summary>包含错误的 chunk</summary>
    public List<AgentStreamChunk> ErrorChunks { get; init; } = [];

    /// <summary>chunk 总数</summary>
    public int ChunkCount => AllChunks.Count;

    /// <summary>是否包含工具调用</summary>
    public bool HasToolCalls => ToolCallChunks.Count > 0;

    /// <summary>是否包含错误</summary>
    public bool HasErrors => ErrorChunks.Count > 0;
}
