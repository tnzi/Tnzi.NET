namespace Tnzi.Tests.AI;

public class ModelSamplingSupportTests
{
    [Theory]
    [InlineData("claude-haiku-5-5")]
    [InlineData("claude-sonnet-5-5")]
    [InlineData("claude-opus-5-5")]
    [InlineData("claude-sonnet-5")]
    [InlineData("claude-opus-6-0")]
    [InlineData("claude-fable-5-1")]
    [InlineData("CLAUDE-SONNET-5-5")]
    [InlineData("anthropic/claude-sonnet-5-5")]
    [InlineData("us.anthropic.claude-haiku-5-5-v1:0")]
    public void NewerClaudeModels_RejectSamplingParameters(string model)
    {
        Assert.False(ModelSamplingSupport.AcceptsSamplingParameters(model));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("claude-sonnet-4-5")]
    [InlineData("claude-opus-4-20250514")]
    [InlineData("claude-3-7-sonnet-20250219")]
    [InlineData("claude-3-5-haiku-latest")]
    [InlineData("gpt-4.1-mini")]
    [InlineData("deepseek-chat")]
    [InlineData("qwen3-max")]
    public void OtherModels_AcceptSamplingParameters(string? model)
    {
        Assert.True(ModelSamplingSupport.AcceptsSamplingParameters(model));
    }
}
