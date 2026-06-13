using Tnzi.Performance.Options;

namespace Tnzi.Performance.Tests;

/// <summary>
/// PerformanceOptions 默认值与验证器测试。
/// Enabled 默认 true（opt-out）：加载 PerformanceModule 即开始采样，
/// 防止"模块已加载但 0 样本"的回归。
/// </summary>
public class PerformanceOptionsTests
{
    [Fact]
    public void Enabled_DefaultsToTrue_SoLoadingTheModuleCollectsSamples()
    {
        var options = new PerformanceOptions();

        options.Enabled.ShouldBeTrue();
        options.MaxHistorySize.ShouldBe(1000);
        options.SlowRequestThresholdMs.ShouldBe(1000);
    }

    [Fact]
    public void Validator_WithDefaults_Passes()
    {
        var validator = new PerformanceOptionsValidator();

        var result = validator.Validate(null, new PerformanceOptions());

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Validator_WhenEnabledWithInvalidHistorySize_Fails()
    {
        var validator = new PerformanceOptionsValidator();

        var result = validator.Validate(null, new PerformanceOptions { MaxHistorySize = 0 });

        result.Failed.ShouldBeTrue();
    }

    [Fact]
    public void Validator_WhenDisabled_SkipsOtherChecks()
    {
        var validator = new PerformanceOptionsValidator();

        var result = validator.Validate(null, new PerformanceOptions { Enabled = false, MaxHistorySize = 0 });

        result.Succeeded.ShouldBeTrue();
    }
}
