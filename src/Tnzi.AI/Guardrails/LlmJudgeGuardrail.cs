namespace Tnzi.AI.Guardrails;

/// <summary>
/// LLM-as-Judge Guardrail - 使用 LLM 评估输入/输出内容的安全性和适当性
/// </summary>
/// <remarks>
/// <para>
/// 同时实现 IInputGuardrail 和 IOutputGuardrail，通过不同的系统提示词分别评估输入和输出。
/// </para>
/// <para>
/// 只有以 <c>PASS</c> 这个词开头的答复才算通过，以 <c>FAIL</c> 开头的一律拒绝。评审<b>不可用</b>
/// （调用出错、超时、答复既不是 PASS 也不是 FAIL）时按 <see cref="GuardrailsOptions.FailClosed"/> 处理：
/// 默认 true 即拒绝，管理员显式关掉才放行。评审必须自己执行这条规则：它吞掉了自己的失败，
/// ToolGuardrailMiddleware 的 fail-closed 分支永远看不到异常，输入 / 输出防护也从不读这个开关，
/// 放行就成了无论开关怎么设都一样的结果。
/// </para>
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

    private const string DefaultRejectionReason = "Content rejected by LLM judge";

    private const string UnavailableReason = "Content could not be verified by the LLM judge";

    private readonly IChatClientFactory _chatClientFactory;
    private readonly IOptionsMonitor<AIOptions> _options;
    private readonly ILogger<LlmJudgeGuardrail> _logger;

    private LlmJudgeOptions JudgeOptions => _options.CurrentValue.Guardrails.LlmJudge;

    public LlmJudgeGuardrail(IChatClientFactory chatClientFactory, IOptionsMonitor<AIOptions> options, ILogger<LlmJudgeGuardrail> logger)
    {
        _chatClientFactory = Check.NotNull(chatClientFactory);
        _options = Check.NotNull(options);
        _logger = Check.NotNull(logger);
    }

    /// <summary>
    /// 验证用户输入内容
    /// </summary>
    async Task<GuardrailResult> IInputGuardrail.ValidateAsync(string input, CancellationToken ct)
    {
        if (!JudgeOptions.Enabled)
        {
            return GuardrailResult.Allowed();
        }

        var systemPrompt = JudgeOptions.InputJudgePrompt ?? DefaultInputJudgePrompt;
        return (await JudgeAsync(input, systemPrompt, ct)).Result;
    }

    /// <summary>
    /// 验证 AI 输出内容
    /// </summary>
    async Task<GuardrailResult> IOutputGuardrail.ValidateAsync(string output, CancellationToken ct)
    {
        if (!JudgeOptions.Enabled)
        {
            return GuardrailResult.Allowed();
        }

        var systemPrompt = JudgeOptions.OutputJudgePrompt ?? DefaultOutputJudgePrompt;
        return (await JudgeAsync(output, systemPrompt, ct)).Result;
    }

    /// <summary>
    /// 调用 LLM 评估内容
    /// </summary>
    private async Task<JudgeVerdict> JudgeAsync(string content, string systemPrompt, CancellationToken ct)
    {
        try
        {
            var chatClient = _chatClientFactory.GetChatClient(JudgeOptions.Provider, JudgeOptions.Model);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(JudgeOptions.TimeoutSeconds));

            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, systemPrompt),
                new(ChatRole.User, content)
            };

            var response = await chatClient.GetResponseAsync(messages, cancellationToken: timeoutCts.Token);
            return ParseJudgeResponse(response.Text ?? string.Empty);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // 超时（非外部取消）：评审不可用
            _logger.LogWarning("LLM-as-Judge timed out after {TimeoutSeconds}s", JudgeOptions.TimeoutSeconds);
            return Unavailable("timeout");
        }
        catch (OperationCanceledException)
        {
            // 外部取消 - 重新抛出
            throw;
        }
        catch (Exception ex)
        {
            // LLM 调用失败：评审不可用
            _logger.LogWarning(ex, "LLM-as-Judge evaluation failed");
            return Unavailable("error");
        }
    }

    /// <summary>
    /// 评审给不出裁决时的结果：<see cref="GuardrailsOptions.FailClosed"/> 为 true（默认）拒绝，否则放行。
    /// </summary>
    private JudgeVerdict Unavailable(string cause)
    {
        if (!_options.CurrentValue.Guardrails.FailClosed)
        {
            _logger.LogWarning("LLM-as-Judge unavailable ({Cause}); allowing content through because Guardrails:FailClosed is false", cause);
            return new JudgeVerdict(GuardrailResult.Allowed(), null);
        }

        _logger.LogWarning("LLM-as-Judge unavailable ({Cause}); rejecting content because Guardrails:FailClosed is true", cause);
        return new JudgeVerdict(GuardrailResult.Rejected(GuardrailName, UnavailableReason), GuardrailReasonCodes.LlmJudgeUnavailable);
    }

    /// <summary>
    /// IGuardrailProvider 实现 - 评估内容安全性
    /// </summary>
    async Task<GuardrailDecision> IGuardrailProvider.EvaluateAsync(GuardrailRequest request, CancellationToken ct)
    {
        if (!JudgeOptions.Enabled || request.Content == null)
        {
            return GuardrailDecision.Allow();
        }

        // 工具级评估（request.ToolName != null）用工具专用提示，否则按输入评估处理
        var systemPrompt = request.ToolName != null
            ? JudgeOptions.ToolJudgePrompt ?? DefaultToolJudgePrompt
            : JudgeOptions.InputJudgePrompt ?? DefaultInputJudgePrompt;
        var verdict = await JudgeAsync(request.Content, systemPrompt, ct);

        return verdict.Result.IsAllowed
            ? GuardrailDecision.Allow()
            : GuardrailDecision.Deny(
                verdict.ReasonCode ?? GuardrailReasonCodes.LlmJudgeRejected,
                verdict.Result.Reason ?? DefaultRejectionReason);
    }

    /// <summary>
    /// 解析 LLM 评估响应。
    /// </summary>
    /// <remarks>
    /// 通过只认第一个词恰好是 <c>PASS</c>（忽略大小写与前导的空白 / 标点，如 <c>**PASS**</c>）：
    /// 「以 PASS 开头」会把 <c>PASSABLE, but ...</c> 这类答复也当成通过。以 <c>FAIL</c> 开头的一律拒绝，
    /// 不论开关怎么设。其余答复（<c>Unsafe: ...</c>、<c>OK</c>、空串）不是裁决，按评审不可用处理。
    /// </remarks>
    private JudgeVerdict ParseJudgeResponse(string responseText)
    {
        var verdict = responseText.TrimStart().TrimStart('*', '_', '`', '"', '\'', '#', '>', '-', ' ');
        var firstWordLength = 0;
        while (firstWordLength < verdict.Length && char.IsLetter(verdict[firstWordLength]))
        {
            firstWordLength++;
        }

        var firstWord = verdict[..firstWordLength];
        if (string.Equals(firstWord, "PASS", StringComparison.OrdinalIgnoreCase))
        {
            return new JudgeVerdict(GuardrailResult.Allowed(), null);
        }

        if (verdict.StartsWith("FAIL", StringComparison.OrdinalIgnoreCase))
        {
            // 提取 "FAIL: <reason>" 中的原因部分
            var reason = verdict[firstWordLength..].Trim().TrimStart(':', '*', ' ').Trim();
            return new JudgeVerdict(
                GuardrailResult.Rejected(GuardrailName, string.IsNullOrWhiteSpace(reason) ? DefaultRejectionReason : reason),
                GuardrailReasonCodes.LlmJudgeRejected);
        }

        return Unavailable("unparseable verdict");
    }

    /// <summary>评审结果与（拒绝时的）原因代码</summary>
    private sealed record JudgeVerdict(GuardrailResult Result, string? ReasonCode);
}
