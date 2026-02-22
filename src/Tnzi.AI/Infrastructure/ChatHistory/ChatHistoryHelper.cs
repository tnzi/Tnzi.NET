namespace Tnzi.AI.Infrastructure.ChatHistory;

/// <summary>
/// 聊天历史辅助工具 — 提供 ChatReducer 共享的静态方法
/// </summary>
internal static class ChatHistoryHelper
{
    /// <summary>
    /// 将消息按对话轮次分组
    /// </summary>
    /// <remarks>
    /// 一轮从用户消息开始，包含后续所有非用户消息直到下一个用户消息
    /// </remarks>
    public static List<List<ChatMessage>> GroupMessagesByTurns(List<ChatMessage> messages)
    {
        var turns = new List<List<ChatMessage>>();
        List<ChatMessage>? currentTurn = null;

        foreach (var message in messages)
        {
            if (message.Role == ChatRole.User)
            {
                currentTurn = [message];
                turns.Add(currentTurn);
            }
            else if (currentTurn != null)
            {
                currentTurn.Add(message);
            }
            else
            {
                // 没有用户消息的助手/工具消息（不常见，但需要处理）
                turns.Add([message]);
            }
        }

        return turns;
    }
}
