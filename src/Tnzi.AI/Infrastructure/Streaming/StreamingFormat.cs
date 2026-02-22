namespace Tnzi.AI.Infrastructure.Streaming;

/// <summary>
/// 流式响应格式
/// </summary>
public enum StreamingFormat
{
    /// <summary>Server-Sent Events (text/event-stream) — OpenAI 兼容格式</summary>
    SSE,
    /// <summary>Newline-delimited JSON (application/x-ndjson)</summary>
    NDJSON
}
