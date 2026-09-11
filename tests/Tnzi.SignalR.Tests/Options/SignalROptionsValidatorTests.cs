namespace Tnzi.SignalR.Tests.Options;

/// <summary>
/// 匿名限流那几项的配置校验：把上限配成 0 与"关掉匿名"是两回事，
/// 前者看起来像个数值调优，实际效果是拒绝每一条匿名连接。
/// </summary>
public class SignalROptionsValidatorTests
{
    private static List<string> Validate(RateLimitOptions rateLimit)
    {
        var errors = new List<string>();
        var validator = new TestableValidator();
        validator.Run(new SignalROptions { RateLimit = rateLimit }, errors);
        return errors;
    }

    /// <summary>ValidateOptions 是 protected，开一个最小的口子把它叫起来。</summary>
    private sealed class TestableValidator : SignalROptionsValidator
    {
        public void Run(SignalROptions options, List<string> errors) => ValidateOptions(options, errors);
    }

    [Fact]
    public void DefaultRateLimitOptionsAreValid()
    {
        Validate(new RateLimitOptions { Enabled = true }).ShouldBeEmpty();
    }

    [Fact]
    public void ZeroAnonymousConnectionCapIsRejectedWithAPointerToTheRealSwitch()
    {
        var errors = Validate(new RateLimitOptions
        {
            Enabled = true,
            MaxConnectionsPerAnonymousPartition = 0,
        });

        errors.ShouldContain(e => e.Contains("MaxConnectionsPerAnonymousPartition", StringComparison.Ordinal)
            && e.Contains("AnonymousPolicy=Reject", StringComparison.Ordinal));
    }

    [Fact]
    public void NonPositiveAnonymousTtlIsRejected()
    {
        var errors = Validate(new RateLimitOptions
        {
            Enabled = true,
            AnonymousConnectionCountTtl = TimeSpan.Zero,
        });

        errors.ShouldContain(e => e.Contains("AnonymousConnectionCountTtl", StringComparison.Ordinal));
    }

    /// <summary>
    /// 不按分区限流时那两项本来就不参与，别拿它们拦住一份合法配置。
    /// </summary>
    [Theory]
    [InlineData(AnonymousHubRateLimitPolicy.Reject)]
    [InlineData(AnonymousHubRateLimitPolicy.Allow)]
    public void AnonymousCapsAreNotCheckedWhenTheyDoNotApply(AnonymousHubRateLimitPolicy policy)
    {
        var errors = Validate(new RateLimitOptions
        {
            Enabled = true,
            AnonymousPolicy = policy,
            MaxConnectionsPerAnonymousPartition = 0,
            AnonymousConnectionCountTtl = TimeSpan.Zero,
        });

        errors.ShouldBeEmpty();
    }

    [Fact]
    public void NothingIsCheckedWhileRateLimitingIsOff()
    {
        Validate(new RateLimitOptions
        {
            Enabled = false,
            MaxConnectionsPerAnonymousPartition = 0,
            MaxMessagesPerMinute = 0,
        }).ShouldBeEmpty();
    }
}
