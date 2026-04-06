namespace Tnzi.AI.Tests.Engine;

/// <summary>
/// 多级错误恢复策略测试
/// </summary>
public class ErrorRecoveryStrategyTests
{
    #region PromptTooLong 恢复

    [Fact]
    public void Analyze_PromptTooLong_FirstAttempt_ReactiveCompact()
    {
        var strategy = new ErrorRecoveryStrategy();
        var ex = new InvalidOperationException("prompt is too long for this model");

        var result = strategy.Analyze(ex);

        result.Action.ShouldBe(ErrorRecoveryStrategy.RecoveryAction.ReactiveCompact);
    }

    [Fact]
    public void Analyze_PromptTooLong_SecondAttempt_Abort()
    {
        var strategy = new ErrorRecoveryStrategy();
        var ex = new InvalidOperationException("prompt is too long for this model");

        strategy.Analyze(ex); // 第一次 → ReactiveCompact
        var result = strategy.Analyze(ex); // 第二次 → Abort

        result.Action.ShouldBe(ErrorRecoveryStrategy.RecoveryAction.Abort);
    }

    [Fact]
    public void Analyze_ContextLengthExceeded_DetectedAsPromptTooLong()
    {
        var strategy = new ErrorRecoveryStrategy();
        var ex = new HttpRequestException("context_length_exceeded: maximum 128000 tokens");

        var result = strategy.Analyze(ex);

        result.Action.ShouldBe(ErrorRecoveryStrategy.RecoveryAction.ReactiveCompact);
    }

    #endregion

    #region MaxOutputTokens 恢复

    [Fact]
    public void Analyze_MaxOutputTokens_FirstAttempt_UpgradeTokens()
    {
        var strategy = new ErrorRecoveryStrategy();
        var ex = new InvalidOperationException("max_output_tokens exceeded");

        var result = strategy.Analyze(ex);

        result.Action.ShouldBe(ErrorRecoveryStrategy.RecoveryAction.UpgradeMaxOutputTokens);
        result.NewMaxOutputTokens.ShouldBe(64_000);
    }

    [Fact]
    public void Analyze_MaxOutputTokens_SecondAttempt_InjectResumeHint()
    {
        var strategy = new ErrorRecoveryStrategy();
        var ex = new InvalidOperationException("max_output_tokens exceeded");

        strategy.Analyze(ex); // L1 → Upgrade
        var result = strategy.Analyze(ex); // L2 → Resume

        result.Action.ShouldBe(ErrorRecoveryStrategy.RecoveryAction.InjectResumeHint);
    }

    [Fact]
    public void Analyze_MaxOutputTokens_ThirdAttempt_Abort()
    {
        var strategy = new ErrorRecoveryStrategy();
        var ex = new InvalidOperationException("max_output_tokens exceeded");

        strategy.Analyze(ex); // L1
        strategy.Analyze(ex); // L2
        var result = strategy.Analyze(ex); // L3 → Abort

        result.Action.ShouldBe(ErrorRecoveryStrategy.RecoveryAction.Abort);
    }

    #endregion

    #region 错误检测

    [Theory]
    [InlineData("prompt is too long")]
    [InlineData("maximum context length exceeded")]
    [InlineData("context_length_exceeded")]
    [InlineData("token limit reached")]
    public void IsPromptTooLongError_KnownPatterns_ReturnsTrue(string message)
    {
        ErrorRecoveryStrategy.IsPromptTooLongError(new Exception(message)).ShouldBeTrue();
    }

    [Theory]
    [InlineData("max_tokens reached")]
    [InlineData("max_output_tokens exceeded")]
    [InlineData("output truncated")]
    public void IsMaxOutputTokensError_KnownPatterns_ReturnsTrue(string message)
    {
        ErrorRecoveryStrategy.IsMaxOutputTokensError(new Exception(message)).ShouldBeTrue();
    }

    [Fact]
    public void Analyze_UnknownError_Abort()
    {
        var strategy = new ErrorRecoveryStrategy();
        var ex = new InvalidOperationException("some random error");

        var result = strategy.Analyze(ex);

        result.Action.ShouldBe(ErrorRecoveryStrategy.RecoveryAction.Abort);
    }

    #endregion
}
