
namespace Tnzi.AI.Engine;

/// <summary>
/// 历史压缩结果，包含诊断指标
/// </summary>
public record HistoryReductionResult(
    List<ChatMessage> Messages,
    int OriginalMessageCount,
    int ReducedMessageCount,
    int EstimatedOriginalTokens,
    int EstimatedReducedTokens,
    string StrategyName)
{
    /// <summary>
    /// Compression ratio (0-1). Lower = more compressed.
    /// </summary>
    public double CompressionRatio => EstimatedOriginalTokens > 0
        ? (double)EstimatedReducedTokens / EstimatedOriginalTokens
        : 1.0;

    /// <summary>
    /// Tokens saved by this reduction
    /// </summary>
    public int TokensSaved => EstimatedOriginalTokens - EstimatedReducedTokens;
}

/// <summary>
/// 历史消息压缩接口
/// </summary>
public interface IHistoryReducer
{
    /// <summary>
    /// 压缩消息列表，返回包含诊断指标的结果
    /// </summary>
    Task<HistoryReductionResult> ReduceAsync(List<ChatMessage> messages, CancellationToken ct = default);
}
