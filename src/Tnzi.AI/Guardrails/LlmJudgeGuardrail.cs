namespace Tnzi.AI.Guardrails;

/// <summary>
/// LLM-as-Judge Guardrail — 使用 LLM 评估输入/输出内容的安全性和适当性
/// </summary>
/// <remarks>
/// 同时实现 IInputGuardrail 和 IOutputGuardrail，通过不同的系统提示词分别评估输入和输出。
/// 采用 fail-open 策略：当 LLM 调用失败或超时时，放行请求以避免阻塞正常操作。
/// </remarks>
[ExperimentalApi(Reason = "LLM-as-Judge guardrail is in preview")]
public class LlmJudgeGuardrail : IInputGuardrail, IOutputGuardrail, IGuardrailProvider
{
    string IGuardrailProvider.Name => nameof(LlmJudgeGuardrail);
    private const string DefaultInputJudgePrompt =
        "Evaluate if this user input is safe and appropriate. Respond with ONLY 'PASS' or 'FAIL: <reason>'";

    private const string DefaultOutputJudgePrompt =
        "Evaluate if this AI response is accurate, safe, and helpful. Respond with ONLY 'PASS' or 'FAIL: <reason>'";

    private const string DefaultToolJudgePrompt =
        "Evaluate whether this tool call should be allowed to execute. Respond with ONLY 'PASS' or 'FAIL: <reason>'";

    private const string GuardrailName = nameof(LlmJudgeGuardrail);

    private readonly IChatClientFactory _chatClientFactory;
    private readonly LlmJudgeOptions _judgeOptions;
    private readonly ILogger<LlmJudgeGuardrail> _logger;

    public LlmJudgeGuardrail(IChatClientFactory chatClientFactory, IOptions<AIOptions> options, ILogger<LlmJudgeGuardrail> logger)
    {
        _chatClientFactory = Check.NotNull(chatClientFactory);
        _judgeOptions = Check.NotNull(options).Value.Guardrails.LlmJudge;
        _logger = Check.NotNull(logger);
    }

    /// <summary>
    /// 验证用户输入内容
    /// </summary>
    async Task<GuardrailResult> IInputGuardrail.ValidateAsync(string input, CancellationToken ct)
    {
        if (!_judgeOptions.Enabled)
        {
            return GuardrailResult.Allowed();
        }

        var systemPrompt = _judgeOptions.InputJudgePrompt ?? DefaultInputJudgePrompt;
        return await JudgeAsync(input, systemPrompt, ct);
    }

    /// <summary>
    /// 验证 AI 输出内容
    /// </summary>
    async Task<GuardrailResult> IOutputGuardrail.ValidateAsync(string output, CancellationToken ct)
    {
        if (!_judgeOptions.Enabled)
        {
            return GuardrailResult.Allowed();
        }

        var systemPrompt = _judgeOptions.OutputJudgePrompt ?? DefaultOutputJudgePrompt;
        return await JudgeAsync(output, systemPrompt, ct);
    }

    /// <summary>
    /// 调用 LLM 评估内容
    /// </summary>
    private async Task<GuardrailResult> JudgeAsync(string content, string systemPrompt, CancellationToken ct)
    {
        try
        {
            var chatClient = _chatClientFactory.GetChatClient(_judgeOptions.Provider, _judgeOptions.Model);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(_judgeOptions.TimeoutSeconds));

            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, systemPrompt),
                new(ChatRole.User, content)
            };

            var response = await chatClient.GetResponseAsync(messages, cancellationToken: timeoutCts.Token);
            var responseText = response.Text?.Trim() ?? string.Empty;

            return ParseJudgeResponse(responseText);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // 超时（非外部取消）— fail-open
            _logger.LogWarning("LLM-as-Judge timed out after {TimeoutSeconds}s, allowing content through (fail-open)", _judgeOptions.TimeoutSeconds);
            return GuardrailResult.Allowed();
        }
        catch (OperationCanceledException)
        {
            // 外部取消 — 重新抛出
            throw;
        }
        catch (Exception ex)
        {
            // LLM 调用失败 — fail-open
            _logger.LogWarning(ex, "LLM-as-Judge evaluation failed, allowing content through (fail-open)");
            return GuardrailResult.Allowed();
        }
    }

    /// <summary>
    /// IGuardrailProvider 实现 — 评估内容安全性
    /// </summary>
    async Task<GuardrailDecision> IGuardrailProvider.EvaluateAsync(GuardrailRequest request, CancellationToken ct)
    {
        if (!_judgeOptions.Enabled || request.Content == null)
        {
            return GuardrailDecision.Allow();
        }

        // 工具级评估（request.ToolName != null）用工具专用提示，否则按输入评估处理
        var systemPrompt = request.ToolName != null
            ? _judgeOptions.ToolJudgePrompt ?? DefaultToolJudgePrompt
            : _judgeOptions.InputJudgePrompt ?? DefaultInputJudgePrompt;
        var result = await JudgeAsync(request.Content, systemPrompt, ct);

        return result.IsAllowed
            ? GuardrailDecision.Allow()
            : GuardrailDecision.Deny(GuardrailReasonCodes.LlmJudgeRejected, result.Reason ?? "Content rejected by LLM judge");
    }

    /// <summary>
    /// 解析 LLM 评估响应
    /// </summary>
    private static GuardrailResult ParseJudgeResponse(string responseText)
    {
        if (responseText.StartsWith("PASS", StringComparison.OrdinalIgnoreCase))
        {
            return GuardrailResult.Allowed();
        }

        if (responseText.StartsWith("FAIL", StringComparison.OrdinalIgnoreCase))
        {
            // 提取 "FAIL: <reason>" 中的原因部分
            var reason = responseText.Length > 5
                ? responseText[5..].TrimStart(':', ' ')
                : "Content rejected by LLM judge";

            if (string.IsNullOrWhiteSpace(reason))
            {
                reason = "Content rejected by LLM judge";
            }

            return GuardrailResult.Rejected(GuardrailName, reason);
        }

        // 无法解析的响应 — 视为通过（fail-open）
        return GuardrailResult.Allowed();
    }
}
