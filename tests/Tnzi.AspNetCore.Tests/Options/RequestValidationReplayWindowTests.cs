using Microsoft.Extensions.Logging.Abstractions;

namespace Tnzi.AspNetCore.Tests.Options;

/// <summary>
/// Nonce 保留时长与时间戳窗口是一对有约束关系的量。
///
/// 时间戳判定是 |服务器时间 − 请求时间| ≤ W，一条抓到的签名请求在其时间戳前后各 W 秒内都合法，
/// 跨度 2W；nonce 只记住 E 秒。E &lt; 2W 时，首次使用后 E 秒到 T+W 之间重放，nonce 已忘、
/// 时间戳仍在窗口内、签名逐字相同 —— 三道检查全过，写端点再执行一次，响应与正常请求无异。
/// 默认 300/600 恰好落在边界上，所以从没暴露；把 W 调到 900 容忍跨机房时钟偏差是常见操作，
/// 而启动校验此前对这两个值只各查 &gt; 0，从不比较。
/// </summary>
public class RequestValidationReplayWindowTests
{
    private static ValidateOptionsResult Validate(RequestValidationOptions validation)
        => new AspNetCoreOptionsValidator()
            .Validate(name: null, new AspNetCoreOptions { RequestValidation = validation });

    [Fact]
    public void DefaultNonceAndWindow_PassValidation()
    {
        // 防锈：出厂值 300/600 恰好满足 E ≥ 2W。
        var result = Validate(new RequestValidationOptions { Enabled = true, RequireNonce = true });

        Assert.True(result.Succeeded, result.FailureMessage);
    }

    [Fact]
    public void NonceTtlShorterThanTwiceTimestampWindow_FailsValidation()
    {
        var result = Validate(new RequestValidationOptions
        {
            Enabled = true,
            RequireNonce = true,
            TimestampWindowSeconds = 900,
            NonceExpirationSeconds = 600
        });

        Assert.True(result.Failed);
        Assert.Contains("NonceExpirationSeconds", result.FailureMessage);
        Assert.Contains("TimestampWindowSeconds", result.FailureMessage);
    }

    [Fact]
    public void NonceRule_IsIgnoredWhenRequireNonceIsFalse()
    {
        // 没开 nonce 就没有 nonce 的保留时长可言；这条约束不该挡住只做时间戳校验的部署。
        var result = Validate(new RequestValidationOptions
        {
            Enabled = true,
            RequireNonce = false,
            TimestampWindowSeconds = 900,
            NonceExpirationSeconds = 600
        });

        Assert.True(result.Succeeded, result.FailureMessage);
    }

    [Fact]
    public async Task NonceTtl_IsNeverShorterThanTheReplayWindow()
    {
        // 第二道保险：即便校验被绕过（消费方自己 Configure 出一份没过校验的实例），
        // 写进缓存的 TTL 也不会短于 2W。
        var cache = new Mock<ICache>();
        TimeSpan? recorded = null;
        cache.Setup(c => c.TrySetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, TimeSpan?, CancellationToken>((_, _, ttl, _) => recorded = ttl)
            .ReturnsAsync(true);

        var options = new AspNetCoreOptions
        {
            RequestValidation = new RequestValidationOptions
            {
                Enabled = true,
                RequireNonce = true,
                TimestampWindowSeconds = 900,
                NonceExpirationSeconds = 600
            }
        };
        var validator = new RequestValidator(
            Microsoft.Extensions.Options.Options.Create(options),
            cache.Object,
            NullLogger<RequestValidator>.Instance);

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/orders";
        context.Request.Headers["X-Timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        context.Request.Headers["X-Nonce"] = Guid.NewGuid().ToString("N");

        var error = await validator.ValidateAsync(context);

        Assert.Null(error);
        Assert.Equal(TimeSpan.FromSeconds(1800), recorded);
    }
}
