namespace Tnzi.AI.Infrastructure.ChatHistory;

/// <summary>
/// 链式历史压缩器 - 按顺序执行多个 IHistoryReducer（如 Prune → Summarize）
/// </summary>
public class ChainedChatReducer : IHistoryReducer
{
    private readonly IReadOnlyList<IHistoryReducer> _reducers;

    public ChainedChatReducer(IReadOnlyList<IHistoryReducer> reducers)
    {
        Check.NotNullOrEmpty(reducers);
        _reducers = reducers;
    }

    public async Task<HistoryReductionResult> ReduceAsync(List<ChatMessage> messages, CancellationToken ct = default)
    {
        if (_reducers.Count == 1)
        {
            return await _reducers[0].ReduceAsync(messages, ct);
        }

        var firstResult = await _reducers[0].ReduceAsync(messages, ct);
        var current = firstResult;
        var strategyNames = new List<string> { firstResult.StrategyName };

        for (int i = 1; i < _reducers.Count; i++)
        {
            current = await _reducers[i].ReduceAsync(current.Messages, ct);
            strategyNames.Add(current.StrategyName);
        }

        return new HistoryReductionResult(
            current.Messages,
            firstResult.OriginalMessageCount,
            current.ReducedMessageCount,
            firstResult.EstimatedOriginalTokens,
            current.EstimatedReducedTokens,
            string.Join("+", strategyNames));
    }
}
