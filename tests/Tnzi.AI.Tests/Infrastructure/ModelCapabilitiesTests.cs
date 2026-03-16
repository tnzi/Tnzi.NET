using Tnzi.AI.Infrastructure.Providers;

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
    [InlineData("claude-haiku-4-5")]
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
    public void SupportsReasoning_OpenAiNonReasoningModels_ReturnsFalse(string model)
    {
        ModelCapabilities.SupportsReasoning(model).ShouldBeFalse();
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
    [InlineData("claude-haiku-4-5")]
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
