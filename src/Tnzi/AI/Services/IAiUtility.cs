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
    /// 回答的口径与 <see cref="ExecuteAsync"/> 不带 <see cref="AiUtilityCallOptions.Provider"/> 时解析提供商的口径相同：
    /// 核心的默认实现只认 <c>AI:Providers</c>（已启用且拿得到 API Key 的那一个）；加载 <c>Tnzi.AI</c> 后
    /// 按 <c>AI:DefaultProvider</c> 经 <c>IChatClientFactory</c> 解析，管理端登记在数据库里的提供商同样计入。
    /// 属性是同步的，实现必须便宜（缓存或内存查找），且在解析失败时答 <see langword="false"/> 而不是抛异常。
    /// </para>
    /// <para>
    /// 它不保证下一次调用一定成功（网络、配额、提供商故障照样会让 <see cref="ExecuteAsync"/> 返回 <see langword="null"/>），
    /// 只保证答 <see langword="false"/> 时调用必然拿不到提供商。调用方自带提供商时不必问它。
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
    /// <returns>AI 回复文本；未配置提供商（或自带的提供商不合法）或调用失败时返回 <see langword="null"/></returns>
    /// <remarks>
    /// ⚠️ <b>输出长度有上限</b>：默认 4096 个 token（<c>AI:Utility:MaxTokens</c>）。超过上限的回答会被<b>静默截断</b>
    /// （不报错、不抛异常，只是话没说完）；需要更长的输出时调高该配置，或用
    /// <see cref="Options.AiUtilityCallOptions.MaxTokens"/> 单次覆盖。标题生成、分类这类极短输出的调用点自己传小值。
    /// <para>
    /// 凭据不在配置里时，用 <see cref="Options.AiUtilityCallOptions.Provider"/> 随本次调用传入提供商，
    /// 完全绕过 <c>AI:Providers</c>；所有实现都必须遵守它（不缓存、不记录其密钥）。
    /// </para>
    /// </remarks>
    Task<string?> ExecuteAsync(
        string systemPrompt,
        string userMessage,
        AiUtilityCallOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 同 <see cref="ExecuteAsync"/>，但返回完整结果：停止原因、token 用量、实际的提供商与模型。
    /// </summary>
    /// <returns>未配置提供商或调用失败时返回 <see langword="null"/>，与 <see cref="ExecuteAsync"/> 同一口径。</returns>
    /// <remarks>
    /// 需要分辨「被截断」「模型没按要求回答」或要按用户计量这次调用的调用方用它。
    /// 默认实现包一层 <see cref="ExecuteAsync"/>：停止原因为 <see cref="AiUtilityFinishReason.Unknown"/>、用量为 null，
    /// 能报出这些的实现应覆盖它。
    /// </remarks>
    async Task<AiUtilityResponse?> ExecuteForResponseAsync(
        string systemPrompt,
        string userMessage,
        AiUtilityCallOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var text = await ExecuteAsync(systemPrompt, userMessage, options, cancellationToken);
        return string.IsNullOrEmpty(text) ? null : new AiUtilityResponse { Text = text, Model = options?.Model };
    }
}
