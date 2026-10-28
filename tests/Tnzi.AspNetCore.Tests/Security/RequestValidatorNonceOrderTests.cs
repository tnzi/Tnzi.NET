using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tnzi.AspNetCore.Tests.Security;

/// <summary>
/// 请求校验在认证与限流之前跑。Nonce 此前在验签之前就写进缓存、且不限长度：
/// 每个带随机大 Nonce、签名乱填的未认证请求都在缓存里留下一条至少 TTL 秒的记录。
/// </summary>
public class RequestValidatorNonceOrderTests
{
    private const string Secret = "unit-test-secret";

    private static (RequestValidator Validator, Mock<ICache> Cache) CreateValidator(bool requireSignature = true)
    {
        var options = new AspNetCoreOptions
        {
            RequestValidation = new RequestValidationOptions
            {
                Enabled = true,
                RequireSignature = requireSignature,
                RequireNonce = true,
                SignatureSecretKey = Secret,
            },
        };
        var cache = new Mock<ICache>();
        var seen = new HashSet<string>();
        cache.Setup(c => c.TrySetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, string _, TimeSpan? _, CancellationToken _) => seen.Add(key));
        return (new RequestValidator(Microsoft.Extensions.Options.Options.Create(options), cache.Object, NullLogger<RequestValidator>.Instance), cache);
    }

    private static DefaultHttpContext Get(string nonce, string? signature = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = "/api/orders";
        context.Request.ContentLength = 0;
        context.Request.Headers["X-Timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        context.Request.Headers["X-Nonce"] = nonce;
        context.Request.Headers["X-Signature"] = signature ?? Sign(context, nonce);
        return context;
    }

    private static string Sign(HttpContext context, string nonce)
    {
        var timestamp = context.Request.Headers["X-Timestamp"].ToString();
        var signString = $"{timestamp}{nonce}{context.Request.Method}{context.Request.Path.Value}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Secret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(signString))).ToLowerInvariant();
    }

    [Fact]
    public async Task ABadSignature_LeavesNoNonceInTheCache()
    {
        var (validator, cache) = CreateValidator();

        var error = await validator.ValidateAsync(Get(Guid.NewGuid().ToString("N"), signature: "deadbeef"));

        Assert.Equal("Request signature validation failed", error);
        cache.Verify(c => c.TrySetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AnOversizedNonce_IsRejectedWithoutACacheWrite()
    {
        var (validator, cache) = CreateValidator(requireSignature: false);

        var error = await validator.ValidateAsync(Get(new string('n', RequestValidator.MaxNonceLength + 1)));

        Assert.NotNull(error);
        Assert.Contains("X-Nonce", error);
        cache.Verify(c => c.TrySetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AValidSignature_WithAReusedNonce_IsStillAReplay()
    {
        // 正控制：挪后不等于不登记
        var (validator, _) = CreateValidator();
        var nonce = Guid.NewGuid().ToString("N");

        Assert.Null(await validator.ValidateAsync(Get(nonce)));
        var replay = await validator.ValidateAsync(Get(nonce));

        Assert.Equal("Request nonce has already been used (replay attack detected)", replay);
    }
}
