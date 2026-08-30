namespace Tnzi.AI.Services;

/// <summary>
/// 轻量级 AI 调用契约 —— 一次性的「系统提示词 + 用户消息 → 文本」，
/// 不涉及工具、技能、会话历史、中间件管线。
/// </summary>
/// <remarks>
/// ★本契约与其默认实现位于框架核心（<c>Tnzi</c> 程序集），**无需加载任何 AI 模块**：
/// 只要在 <c>AI:Providers</c> 下配置了提供商，注入本接口即可调用 LLM，
/// 不会引入任何实体表、控制器或权限码。
/// <para>
/// 默认实现 <c>OpenAiCompatibleAiUtility</c> 直接走 OpenAI 兼容的
/// <c>/chat/completions</c> 端点，零第三方 SDK 依赖，覆盖 OpenAI / DeepSeek / Kimi /
/// GLM / MiniMax / 通义 / Ollama / vLLM 等绝大多数提供商。
/// 加载 <c>Tnzi.AI</c> 模块后，实现会被替换为基于 <c>Microsoft.Extensions.AI</c> 的版本，
/// 从而支持原生 Anthropic 协议、思考内容、降级链等能力 —— 本接口的语义不变。
/// </para>
/// <para>
/// 需要工具调用、多轮会话、Agent 编排时，改用 <c>Tnzi.AI</c> 的 <c>ITnziAiClient</c>
/// 或 <c>IAgentRuntime</c>。
/// </para>
/// </remarks>
[ExperimentalApi(Reason = "AI abstractions are evolving")]
public interface IAiUtility
{
    /// <summary>
    /// 此刻有没有可用的提供商 —— 调用方据此决定「要不要把 AI 入口显示出来」。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 存在的理由是「显示了按钮、点了没反应」这类失败：<see cref="ExecuteAsync"/> 在没有
    /// 可用提供商时返回 <see langword="null"/>，与「调用失败」不可区分，调用方无法据此
    /// 提前把入口藏起来。同一考量见 <see cref="Tnzi.Documents.IDocumentConverter.IsAvailable"/>。
    /// </para>
    /// <para>
    /// <b>方向刻意偏保守</b>：属性是同步的、不查库，所以只能看配置来源的提供商。
    /// 加载 <c>Tnzi.AI</c> 后仍可能存在只在数据库里登记的提供商，那种情形本属性会低报
    /// （少显示一个本可用的入口），而不会高报（显示一个用不了的入口）。
    /// </para>
    /// <para>默认 <see langword="true"/>：自带凭据、无需配置的自定义实现无需覆盖。</para>
    /// </remarks>
    bool IsAvailable => true;

    /// <summary>
    /// 执行一次简单的 AI 请求
    /// </summary>
    /// <param name="systemPrompt">系统提示词</param>
    /// <param name="userMessage">用户消息</param>
    /// <param name="options">单次调用覆盖选项</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>AI 回复文本；未配置提供商或调用失败时返回 <see langword="null"/></returns>
    /// <remarks>
    /// ⚠️ <b>输出长度上限默认只有 100 个 token</b>（<c>AI:Utility:MaxTokens</c>）——
    /// 这个默认值是为框架内部的标题生成、分类这类极短输出定的。要一段完整的回答，
    /// <b>必须</b>调高该配置，或用 <see cref="Options.AiUtilityCallOptions.MaxTokens"/> 单次覆盖，
    /// 否则回答会被<b>静默截断</b>（不报错、不抛异常，只是话没说完）。
    /// </remarks>
    Task<string?> ExecuteAsync(
        string systemPrompt,
        string userMessage,
        AiUtilityCallOptions? options = null,
        CancellationToken cancellationToken = default);
}
