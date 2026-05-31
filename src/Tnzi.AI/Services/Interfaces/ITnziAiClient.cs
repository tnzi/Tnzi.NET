namespace Tnzi.AI.Services.Interfaces;

/// <summary>
/// 嵌入式 AI 客户端 — 绕过 HTTP，直接调用 IAgentRuntime。
/// 适用于后台任务（Hangfire）、CLI、集成测试、IM Channel Bridge。
/// </summary>
[StableApi(Since = "0.1.0")]
public interface ITnziAiClient
{
    /// <summary>发送消息并获取完整响应（非流式）</summary>
    Task<AiClientResponse> ChatAsync(
        string message,
        Guid? threadId = null,
        AiClientOptions? options = null,
        CancellationToken ct = default);

    /// <summary>发送消息并获取流式响应</summary>
    IAsyncEnumerable<AiClientStreamEvent> ChatStreamingAsync(
        string message,
        Guid? threadId = null,
        AiClientOptions? options = null,
        CancellationToken ct = default);

    /// <summary>创建新线程</summary>
    Task<Guid> CreateThreadAsync(string? title = null, CancellationToken ct = default);

    /// <summary>删除线程</summary>
    Task DeleteThreadAsync(Guid threadId, CancellationToken ct = default);
}
