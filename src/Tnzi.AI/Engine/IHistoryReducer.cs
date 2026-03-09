
namespace Tnzi.AI.Engine;

/// <summary>
/// 历史消息压缩接口
/// </summary>
public interface IHistoryReducer
{
    /// <summary>
    /// 压缩消息列表
    /// </summary>
    /// <param name="messages">原始消息列表</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>压缩后的消息列表</returns>
    Task<List<ChatMessage>> ReduceAsync(List<ChatMessage> messages, CancellationToken ct = default);
}
