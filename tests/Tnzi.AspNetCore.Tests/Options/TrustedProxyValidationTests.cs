namespace Tnzi.AspNetCore.Tests.Options;

/// <summary>
/// 受信代理声明的启动期校验。
///
/// 守的是一条无症状的线：写错的代理地址不会让任何东西报错，
/// 只是那一跳不再受信 —— 于是所有调用方共用代理的地址，
/// 限流把所有人算进一个桶。运维读到的现象是「限流太严」，
/// 而不是「配置里有个拼错的地址」，两者相隔很远。
/// </summary>
public class TrustedProxyValidationTests
{
    private static ValidateOptionsResult Validate(TrustedProxyOptions trusted)
        => new AspNetCoreOptionsValidator()
            .Validate(name: null, new AspNetCoreOptions { TrustedProxies = trusted });

    [Fact]
    public void TheDefaultDeclaration_IsValid()
    {
        // 防锈：绝大多数部署一行都不配，这条保证校验不会挡住它们。
        Assert.True(Validate(new TrustedProxyOptions()).Succeeded);
    }

    [Theory]
    [InlineData("not-an-ip")]
    [InlineData("10.0.0.256")]
    [InlineData("10.0.0.0/8")]
    public void AMalformedProxyAddress_IsRejected(string proxy)
    {
        var result = Validate(new TrustedProxyOptions { KnownProxies = [proxy] });

        Assert.True(result.Failed);
        Assert.Contains("KnownProxies", result.FailureMessage);
    }

    [Theory]
    [InlineData("10.0.0.0")]
    [InlineData("10.0.0.0/x")]
    [InlineData("not-a-network/8")]
    public void AMalformedNetwork_IsRejected(string network)
    {
        var result = Validate(new TrustedProxyOptions { KnownNetworks = [network] });

        Assert.True(result.Failed);
        Assert.Contains("KnownNetworks", result.FailureMessage);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ANonPositiveForwardLimit_IsRejected(int limit)
    {
        // 0 层等于「转发头一律不采信」，配了代理却写 0 一定是笔误。
        // 想不限层数的写 null，那是一个不同的意思。
        var result = Validate(new TrustedProxyOptions { ForwardLimit = limit });

        Assert.True(result.Failed);
        Assert.Contains("ForwardLimit", result.FailureMessage);
    }

    [Fact]
    public void AnUnlimitedForwardChain_IsAllowed()
    {
        Assert.True(Validate(new TrustedProxyOptions { ForwardLimit = null }).Succeeded);
    }

    [Fact]
    public void AWellFormedDeclaration_Passes()
    {
        var result = Validate(new TrustedProxyOptions
        {
            KnownProxies = ["10.0.0.8", "2001:db8::1"],
            KnownNetworks = ["10.0.0.0/8", "2001:db8::/32"],
            ForwardLimit = 2
        });

        Assert.True(result.Succeeded);
    }
}
