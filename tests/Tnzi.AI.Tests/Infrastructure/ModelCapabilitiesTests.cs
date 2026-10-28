
namespace Tnzi.AI.Tests.Infrastructure;

/// <summary>
/// ModelCapabilities 静态类单元测试
/// </summary>
public class ModelCapabilitiesTests
{
    #region SupportsReasoning

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void SupportsReasoning_NullOrEmpty_ReturnsFalse(string? model)
    {
        ModelCapabilities.SupportsReasoning(model).ShouldBeFalse();
    }

    [Theory]
    [InlineData("o1-preview")]
    [InlineData("o3-mini")]
    [InlineData("o4-mini")]
    [InlineData("o1")]
    [InlineData("o3")]
    public void SupportsReasoning_OpenAIOrSeries_ReturnsTrue(string model)
    {
        ModelCapabilities.SupportsReasoning(model).ShouldBeTrue();
    }

    [Theory]
    [InlineData("deepseek-reasoner")]
    [InlineData("deepseek-r1")]
    [InlineData("deepseek-r1-preview")]
    public void SupportsReasoning_DeepSeek_ReturnsTrue(string model)
    {
        ModelCapabilities.SupportsReasoning(model).ShouldBeTrue();
    }

    [Theory]
    [InlineData("gemini-2.5-flash")]
    [InlineData("gemini-2.5-pro")]
    [InlineData("gemini-3-pro")]
    public void SupportsReasoning_Gemini_ReturnsTrue(string model)
    {
        ModelCapabilities.SupportsReasoning(model).ShouldBeTrue();
    }

    [Theory]
    [InlineData("qwq-32b")]
    [InlineData("qwq-plus")]
    public void SupportsReasoning_QwQ_ReturnsTrue(string model)
    {
        ModelCapabilities.SupportsReasoning(model).ShouldBeTrue();
    }

    [Theory]
    [InlineData("qwen3-72b")]
    [InlineData("qwen3-235b")]
    public void SupportsReasoning_Qwen3_ReturnsTrue(string model)
    {
        ModelCapabilities.SupportsReasoning(model).ShouldBeTrue();
    }

    [Theory]
    [InlineData("grok-3-mini-beta")]
    [InlineData("grok-3-mini")]
    public void SupportsReasoning_Grok3Mini_ReturnsTrue(string model)
    {
        ModelCapabilities.SupportsReasoning(model).ShouldBeTrue();
    }

    [Theory]
    [InlineData("claude-opus-4-1-20250805")]
    [InlineData("claude-opus-4-20250514")]
    [InlineData("claude-sonnet-4-20250514")]
    [InlineData("claude-3-7-sonnet-20250219")]
    public void SupportsReasoning_ClaudeThinkingModels_ReturnsTrue(string model)
    {
        ModelCapabilities.SupportsReasoning(model).ShouldBeTrue();
    }

    [Theory]
    [InlineData("claude-3-sonnet")]
    [InlineData("claude-3-5-sonnet")]
    [InlineData("claude-3-5-haiku")]
    [InlineData("grok-3")]
    [InlineData("gemini-1.5-pro")]
    [InlineData("qwen2-72b")]
    [InlineData("deepseek-chat")]
    public void SupportsReasoning_NonReasoningModels_ReturnsFalse(string model)
    {
        ModelCapabilities.SupportsReasoning(model).ShouldBeFalse();
    }

    [Theory]
    [InlineData("gpt-4o")]
    [InlineData("gpt-4.1")]
    [InlineData("gpt-5-chat-latest")]
    [InlineData("gpt-5.1-chat-latest")]
    public void SupportsReasoning_OpenAiNonReasoningModels_ReturnsFalse(string model)
    {
        ModelCapabilities.SupportsReasoning(model).ShouldBeFalse();
    }

    /// <summary>GPT-5 家族与 gpt-oss 收 reasoning_effort；此前表里没有它们，配置的推理强度被静默跳过。</summary>
    [Theory]
    [InlineData("gpt-5")]
    [InlineData("gpt-5-mini")]
    [InlineData("gpt-5-nano")]
    [InlineData("gpt-5.1")]
    [InlineData("gpt-oss-120b")]
    public void SupportsReasoning_OpenAiGpt5AndGptOss_ReturnsTrue(string model)
    {
        ModelCapabilities.SupportsReasoning(model).ShouldBeTrue();
        ModelCapabilities.SupportsEffortControl(model).ShouldBeTrue();
        ModelCapabilities.IsAlwaysOnReasoning(model).ShouldBeFalse();
    }

    /// <summary>Claude Haiku 4.5 按 budget_tokens 支持扩展思考，与 Sonnet 4 / Opus 4 同一形态。</summary>
    [Theory]
    [InlineData("claude-haiku-4-5")]
    [InlineData("claude-haiku-4-5-20251001")]
    public void SupportsReasoning_ClaudeHaiku45_ReturnsTrue(string model)
    {
        ModelCapabilities.SupportsReasoning(model).ShouldBeTrue();
        ModelCapabilities.SupportsEffortControl(model).ShouldBeTrue();
    }

    /// <summary>
    /// 4.7 起的 Claude 型号走 adaptive。此前表里只收 budget_tokens 形态的型号：5 系列整个不在表里（推理被跳过），
    /// opus-4-7 / opus-4-8 又被 <c>claude-opus-4</c> 前缀误收、按 budget_tokens 请求而被 API 以 400 拒绝。
    /// </summary>
    [Theory]
    [InlineData("claude-opus-4-7")]
    [InlineData("claude-opus-4-8")]
    [InlineData("claude-opus-5")]
    [InlineData("claude-opus-5-5")]
    [InlineData("claude-sonnet-5")]
    [InlineData("claude-sonnet-5-5")]
    [InlineData("claude-haiku-5-5")]
    [InlineData("claude-fable-5")]
    [InlineData("claude-fable-5-1")]
    [InlineData("claude-mythos-5-1")]
    [InlineData("CLAUDE-OPUS-5-5")]
    public void ClaudeAdaptiveModels_SupportReasoningInTheAdaptiveForm(string model)
    {
        ModelCapabilities.SupportsReasoning(model).ShouldBeTrue();
        ModelCapabilities.SupportsEffortControl(model).ShouldBeTrue();
        ModelCapabilities.UsesClaudeAdaptiveThinking(model).ShouldBeTrue();
        ModelCapabilities.IsAlwaysOnReasoning(model).ShouldBeFalse();
    }

    /// <summary>
    /// 4.6 之前的型号只认 budget_tokens，拒收 adaptive；4.6 两种都收，取 extended（它的 adaptive effort 没有
    /// xhigh，框架的 Max 档会被拒）。日期戳（8 位数字）不能被读成次版本号：
    /// <c>claude-opus-4-20250514</c> 是 4.0，不是 4.20250514。
    /// </summary>
    [Theory]
    [InlineData("claude-opus-4-6")]
    [InlineData("claude-sonnet-4-6")]
    [InlineData("claude-3-7-sonnet-20250219")]
    [InlineData("claude-opus-4-20250514")]
    [InlineData("claude-sonnet-4-20250514")]
    [InlineData("claude-opus-4-1-20250805")]
    [InlineData("claude-opus-4-5")]
    [InlineData("claude-opus-4-5-20251101")]
    [InlineData("claude-sonnet-4-5")]
    [InlineData("claude-haiku-4-5-20251001")]
    public void ClaudeBudgetTokenModels_SupportReasoningInTheExtendedForm(string model)
    {
        ModelCapabilities.SupportsReasoning(model).ShouldBeTrue();
        ModelCapabilities.UsesClaudeAdaptiveThinking(model).ShouldBeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("gpt-5")]
    [InlineData("claude-3-5-sonnet")]
    [InlineData("deepseek-reasoner")]
    public void UsesClaudeAdaptiveThinking_NonAdaptiveOrNonClaude_ReturnsFalse(string? model)
    {
        ModelCapabilities.UsesClaudeAdaptiveThinking(model).ShouldBeFalse();
    }

    #endregion

    #region IsAlwaysOnReasoning

    [Theory]
    [InlineData("deepseek-reasoner")]
    [InlineData("deepseek-r1")]
    [InlineData("deepseek-r1-preview")]
    public void IsAlwaysOnReasoning_DeepSeek_ReturnsTrue(string model)
    {
        ModelCapabilities.IsAlwaysOnReasoning(model).ShouldBeTrue();
    }

    [Theory]
    [InlineData("qwq-32b")]
    [InlineData("qwq-plus")]
    public void IsAlwaysOnReasoning_QwQ_ReturnsTrue(string model)
    {
        ModelCapabilities.IsAlwaysOnReasoning(model).ShouldBeTrue();
    }

    [Theory]
    [InlineData("o4-mini")]
    [InlineData("o1-preview")]
    [InlineData("gemini-2.5-flash")]
    [InlineData("qwen3-72b")]
    [InlineData("claude-3-thinking")]
    public void IsAlwaysOnReasoning_NonAlwaysOnModels_ReturnsFalse(string model)
    {
        ModelCapabilities.IsAlwaysOnReasoning(model).ShouldBeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void IsAlwaysOnReasoning_NullOrEmpty_ReturnsFalse(string? model)
    {
        ModelCapabilities.IsAlwaysOnReasoning(model).ShouldBeFalse();
    }

    #endregion

    #region SupportsEffortControl

    [Theory]
    [InlineData("o1-preview")]
    [InlineData("o3-mini")]
    [InlineData("o4-mini")]
    public void SupportsEffortControl_OpenAIOrSeries_ReturnsTrue(string model)
    {
        ModelCapabilities.SupportsEffortControl(model).ShouldBeTrue();
    }

    [Theory]
    [InlineData("gemini-2.5-flash")]
    [InlineData("gemini-2.5-pro")]
    [InlineData("gemini-3-pro")]
    public void SupportsEffortControl_Gemini_ReturnsTrue(string model)
    {
        ModelCapabilities.SupportsEffortControl(model).ShouldBeTrue();
    }

    [Theory]
    [InlineData("qwen3-72b")]
    [InlineData("qwen3-235b")]
    public void SupportsEffortControl_Qwen3_ReturnsTrue(string model)
    {
        ModelCapabilities.SupportsEffortControl(model).ShouldBeTrue();
    }

    [Theory]
    [InlineData("claude-opus-4-1-20250805")]
    [InlineData("claude-opus-4-20250514")]
    [InlineData("claude-sonnet-4-20250514")]
    [InlineData("claude-3-7-sonnet-20250219")]
    public void SupportsEffortControl_ClaudeThinkingModels_ReturnsTrue(string model)
    {
        ModelCapabilities.SupportsEffortControl(model).ShouldBeTrue();
    }

    [Theory]
    [InlineData("deepseek-reasoner")]
    [InlineData("deepseek-r1")]
    public void SupportsEffortControl_DeepSeek_ReturnsFalse(string model)
    {
        ModelCapabilities.SupportsEffortControl(model).ShouldBeFalse();
    }

    [Theory]
    [InlineData("qwq-32b")]
    [InlineData("qwq-plus")]
    public void SupportsEffortControl_QwQ_ReturnsFalse(string model)
    {
        ModelCapabilities.SupportsEffortControl(model).ShouldBeFalse();
    }

    [Theory]
    [InlineData("grok-3-mini")]
    [InlineData("grok-3-mini-beta")]
    [InlineData("claude-3-sonnet")]
    [InlineData("claude-3-5-sonnet")]
    [InlineData("gpt-5-chat-latest")]
    public void SupportsEffortControl_UnsupportedModels_ReturnsFalse(string model)
    {
        ModelCapabilities.SupportsEffortControl(model).ShouldBeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void SupportsEffortControl_NullOrEmpty_ReturnsFalse(string? model)
    {
        ModelCapabilities.SupportsEffortControl(model).ShouldBeFalse();
    }

    #endregion
}
