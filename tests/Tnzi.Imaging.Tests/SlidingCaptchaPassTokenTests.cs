using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tnzi.AspNetCore.Security;
using Tnzi.ScopedContext;

namespace Tnzi.Imaging.Tests;

/// <summary>
/// 滑块验证通过之后签出的通行令牌：这是滑块从「孤立控件」变成一家 <see cref="ICaptchaProvider"/> 的那一环。
/// </summary>
/// <remarks>
/// 此前 <c>/captcha/sliding/verify</c> 只回 <c>{ Success }</c>，受保护端点无从分辨这次提交前有没有真的滑过 ——
/// 一个跳过滑块直接 POST 的机器人与一个滑过的人在服务端看来一模一样。
/// 用真实的内存缓存跑（不是 mock）：一次性与「删了再判」两条都是缓存操作顺序的问题，mock 证明不了。
/// </remarks>
public class SlidingCaptchaPassTokenTests
{
    private static (SlidingCaptchaService Service, ICache Cache) CreateService()
    {
        var scopedContext = new Mock<IScopedContext>();
        scopedContext.Setup(c => c.ClientIpAddress).Returns("203.0.113.7");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(scopedContext.Object);
        var provider = services.BuildServiceProvider();

        var cache = new MemoryCacheService(
            new MemoryCache(new MemoryCacheOptions()),
            Mock.Of<ILogger<MemoryCacheService>>(),
            Microsoft.Extensions.Options.Options.Create(new CachingOptions()),
            provider);

        var options = Microsoft.Extensions.Options.Options.Create(new ImagingOptions());
        return (new SlidingCaptchaService(provider, options, cache), cache);
    }

    /// <summary>生成一道题并直接读出正确答案（测试只关心令牌那一环，不关心图片）。</summary>
    private static async Task<(string Token, int CorrectX)> GenerateAsync(SlidingCaptchaService service, ICache cache, string? purpose)
    {
        var generated = await service.GenerateAsync(purpose: purpose);
        Assert.True(generated.Succeeded);
        var stored = await cache.GetAsync<SlidingCaptchaStoredData>($"SlidingCaptcha:{generated.Data!.Token}");
        Assert.NotNull(stored);
        return (generated.Data.Token, stored!.CorrectX);
    }

    [Fact]
    public async Task SuccessfulVerify_IssuesAPassToken_FailedVerifyDoesNot()
    {
        var (service, cache) = CreateService();
        var (token, correctX) = await GenerateAsync(service, cache, "login");

        var passed = await service.VerifyAsync(token, correctX);

        Assert.True(passed.Data!.Success);
        Assert.False(string.IsNullOrEmpty(passed.Data.PassToken));

        var (token2, correctX2) = await GenerateAsync(service, cache, "login");
        var failed = await service.VerifyAsync(token2, correctX2 + 100);

        Assert.False(failed.Data!.Success);
        Assert.Null(failed.Data.PassToken);
    }

    [Fact]
    public async Task PassToken_RedeemsOnce_UnderTheBoundPurpose()
    {
        var (service, cache) = CreateService();
        var (token, correctX) = await GenerateAsync(service, cache, "login");
        var pass = (await service.VerifyAsync(token, correctX)).Data!.PassToken!;

        Assert.True(await service.RedeemPassTokenAsync(pass, "login"));
        Assert.False(await service.RedeemPassTokenAsync(pass, "login"));
    }

    [Fact]
    public async Task PassToken_BoundToAnotherPurpose_IsRefused_AndBurned()
    {
        var (service, cache) = CreateService();
        var (token, correctX) = await GenerateAsync(service, cache, "register");
        var pass = (await service.VerifyAsync(token, correctX)).Data!.PassToken!;

        Assert.False(await service.RedeemPassTokenAsync(pass, "login"));
        // 试错的那一次已经把它烧掉：拿一枚令牌逐个用途试是不允许的。
        Assert.False(await service.RedeemPassTokenAsync(pass, "register"));
    }

    [Fact]
    public async Task PassToken_WithoutABoundPurpose_IsAcceptedForAnyPurpose()
    {
        var (service, cache) = CreateService();
        var (token, correctX) = await GenerateAsync(service, cache, purpose: null);
        var pass = (await service.VerifyAsync(token, correctX)).Data!.PassToken!;

        Assert.True(await service.RedeemPassTokenAsync(pass, "contact"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("never-issued")]
    public async Task UnknownOrBlankPassToken_IsRefused(string pass)
    {
        var (service, _) = CreateService();

        Assert.False(await service.RedeemPassTokenAsync(pass, "login"));
    }

    [Fact]
    public async Task Provider_MapsRedeemToPassAndExpiredOrReplayed()
    {
        var (service, cache) = CreateService();
        var provider = new SlidingCaptchaProvider(service);
        var (token, correctX) = await GenerateAsync(service, cache, "login");
        var pass = (await service.VerifyAsync(token, correctX)).Data!.PassToken!;

        var first = await provider.VerifyAsync(new CaptchaVerificationRequest(pass, "login", null));
        var second = await provider.VerifyAsync(new CaptchaVerificationRequest(pass, "login", null));

        Assert.Equal("sliding", provider.Name);
        Assert.True(first.Passed);
        Assert.Equal("login", first.Action);
        Assert.False(second.Passed);
        Assert.Equal(CaptchaFailure.ExpiredOrReplayed, second.Failure);
        Assert.Contains("{purpose}", provider.GetClientConfig().ChallengeUrl, StringComparison.Ordinal);
    }
}
