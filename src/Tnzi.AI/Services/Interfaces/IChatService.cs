namespace Tnzi.AI.Services.Interfaces;

/// <summary>
/// 聊天服务接口
/// </summary>
public interface IChatService
{
    /// <summary>
    /// 发送消息
    /// </summary>
    Task<Result<ChatResponseDto>> ChatAsync(ChatRequestDto request, CancellationToken ct = default);

    /// <summary>
    /// 流式发送消息（delta 模型 — 每个事件只包含增量内容）
    /// </summary>
    IAsyncEnumerable<StreamEvent> ChatStreamingAsync(ChatRequestDto request, CancellationToken ct = default);
}
