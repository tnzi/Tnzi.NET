namespace Tnzi.AI.Infrastructure.Providers;

/// <summary>
/// MiniMax 消息处理器 - 处理 inline &lt;think&gt; 标签和 reasoning_details 字段。
/// </summary>
/// <remarks>
/// MiniMax 模型在流式和非流式响应中使用 inline &lt;think&gt;...&lt;/think&gt; 标签包裹推理内容，
/// 以及 reasoning_details/reasoning_split 字段。此处理器清理这些标签，
/// 将推理内容提取到 AdditionalProperties 中以保持消息格式一致。
/// </remarks>
public class MiniMaxChatMessageProcessor : ThinkTagChatMessageProcessorBase
{
    public override string ProviderName => "minimax";
}
