namespace Tnzi.AI.Infrastructure.Providers;

/// <summary>
/// GLM (智谱清言/Zhipu) 消息处理器 - 处理 inline &lt;think&gt; 标签。
/// </summary>
/// <remarks>
/// GLM Z1 系列模型在推理模式下使用 &lt;think&gt;...&lt;/think&gt; 标签包裹思考过程。
/// 此处理器清理这些标签，将推理内容提取到 AdditionalProperties 中以保持消息格式一致。
/// </remarks>
public class GlmChatMessageProcessor : ThinkTagChatMessageProcessorBase
{
    public override string ProviderName => "glm";
}
