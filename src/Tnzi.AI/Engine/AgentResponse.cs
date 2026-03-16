
namespace Tnzi.AI.Engine;

/// <summary>
/// Agent 非流式执行结果
/// </summary>
public class AgentResponse
{
    /// <summary>
    /// 助手回复的文本内容
    /// </summary>
    public string? Text { get; set; }

    /// <summary>
    /// Token 使用信息
    /// </summary>
    public TokenUsageDto? Usage { get; set; }

    /// <summary>
    /// 结束原因
    /// </summary>
    public string? FinishReason { get; set; }

    /// <summary>
    /// 完整消息列表（含对话上下文，可用于持久化）
    /// </summary>
    public List<ChatMessage> Messages { get; set; } = [];

    /// <summary>
    /// RAG 引用来源（来自 ContextProvider 的结构化 Citation 数据）
    /// </summary>
    public List<CitationDto>? Citations { get; set; }

    /// <summary>
    /// 推理/思考过程内容（从 TextReasoningContent 提取）
    /// </summary>
    public string? Reasoning { get; set; }
}
