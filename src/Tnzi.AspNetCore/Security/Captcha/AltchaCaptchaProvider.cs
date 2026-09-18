namespace Tnzi.AspNetCore.Security;

/// <summary>
/// Altcha：自托管的工作量证明验证码，服务端出题、客户端算哈希、服务端验签，**不调用任何外部服务**。
/// MIT 协议，GFW 内可用，零新增 NuGet（SHA-256 与 HMAC 都是 in-box 的）。
/// </summary>
/// <remarks>
/// <para>
/// 出题：<c>challenge = hex(sha256(salt + number))</c>，<c>signature = hex(hmac(key, challenge))</c>，
/// salt 带 <c>?expires=</c> 与 <c>&amp;purpose=</c>。服务端什么都不存 —— 有效期与用途都在 salt 里，
/// 而 salt 被 challenge 覆盖、challenge 被签名覆盖，改任何一处都验不过。
/// </para>
/// <para>
/// 验证：客户端交回 base64 JSON <c>{ algorithm, challenge, number, salt, signature }</c>。依次核对算法、
/// 重算 challenge、验签（恒定时间比较）、有效期、用途，最后<b>把 challenge 记进缓存直到过期</b>：
/// 托管型提供商的一次性由它们的验证服务保证，这里没有那个人，一枚解对的载荷在有效期内可以重放，
/// 所以必须自己记。<see cref="ICache"/> 是必需依赖，不做「没缓存就放过重放」的降级。
/// </para>
/// </remarks>
public class AltchaCaptchaProvider : ICaptchaProvider
{
    /// <summary>提供商名。</summary>
    public const string ProviderName = "altcha";

    /// <summary>
    /// 默认控件脚本（jsDelivr，<b>钉在 3.x 大版本线</b>）。自托管改 <c>AspNetCore:Captcha:ScriptUrl</c>。
    /// </summary>
    /// <remarks>
    /// 前端驱动（<c>@tnzi/core</c> 的 <c>mountCaptchaWidget</c>）按 3.x 的属性契约挂控件（出题地址写在 <c>challenge</c>
    /// 属性上；2.x 的 <c>challengeurl</c> 也一并写着）。不钉大版本的地址会随 npm latest 漂过驱动会说的那一版，
    /// 而症状只在浏览器里出现：构建与启动都不报错，控件却拿不到题。2026-09-17 实发于 3.x（当时地址不带版本，
    /// 驱动只写 2.x 的属性名）。
    /// </remarks>
    public const string DefaultScriptUrl = "https://cdn.jsdelivr.net/npm/altcha@3/dist/altcha.min.js";

    /// <summary>挑战端点模板（相对 API 根，<c>{purpose}</c> 由客户端替换）。</summary>
    public const string ChallengePath = "captcha/altcha/challenge?purpose={purpose}";

    private const string Algorithm = "SHA-256";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ICache _cache;
    private readonly AltchaOptions _options;
    private readonly string _scriptUrl;
    private readonly ILogger<AltchaCaptchaProvider> _logger;
    private readonly TimeProvider _clock;

    /// <summary>
    /// 初始化提供商。
    /// </summary>
    public AltchaCaptchaProvider(
        ICache cache,
        IOptionsSnapshot<CaptchaVerifierOptions> options,
        ILogger<AltchaCaptchaProvider> logger,
        TimeProvider? clock = null)
    {
        _cache = Check.NotNull(cache);
        var all = Check.NotNull(options).Value;
        _options = all.Altcha;
        _scriptUrl = string.IsNullOrWhiteSpace(all.ScriptUrl) ? DefaultScriptUrl : all.ScriptUrl;
        _logger = Check.NotNull(logger);
        _clock = clock ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public string Name => ProviderName;

    /// <summary>
    /// 出一道题。<paramref name="purpose"/> 写进 salt，验证时必须与端点声明的用途一致。
    /// </summary>
    public AltchaChallengeDto CreateChallenge(string purpose)
    {
        Check.NotNullOrWhiteSpace(purpose);
        var key = RequireKey();

        var expires = _clock.GetUtcNow().ToUnixTimeSeconds() + _options.ExpiresSeconds;
        var salt = $"{Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant()}?expires={expires}&purpose={Uri.EscapeDataString(purpose)}";
        var number = RandomNumberGenerator.GetInt32(_options.MaxNumber + 1);
        var challenge = Sha256Hex(salt + number.ToString(CultureInfo.InvariantCulture));

        return new AltchaChallengeDto
        {
            Algorithm = Algorithm,
            Challenge = challenge,
            MaxNumber = _options.MaxNumber,
            Salt = salt,
            Signature = HmacHex(key, challenge)
        };
    }

    /// <inheritdoc />
    public async Task<CaptchaVerification> VerifyAsync(CaptchaVerificationRequest request, CancellationToken cancellationToken = default)
    {
        Check.NotNull(request);
        var key = RequireKey();

        AltchaPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<AltchaPayload>(Convert.FromBase64String(request.Token), JsonOptions);
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return CaptchaVerification.Fail(Name, CaptchaFailure.Rejected, "Malformed payload");
        }

        if (payload == null || string.IsNullOrEmpty(payload.Challenge) || string.IsNullOrEmpty(payload.Salt) || string.IsNullOrEmpty(payload.Signature))
            return CaptchaVerification.Fail(Name, CaptchaFailure.Rejected, "Incomplete payload");

        if (!string.Equals(payload.Algorithm, Algorithm, StringComparison.OrdinalIgnoreCase))
            return CaptchaVerification.Fail(Name, CaptchaFailure.Rejected, $"Unsupported algorithm {payload.Algorithm}");

        var expectedChallenge = Sha256Hex(payload.Salt + payload.Number.ToString(CultureInfo.InvariantCulture));
        if (!FixedTimeEqualsHex(expectedChallenge, payload.Challenge))
            return CaptchaVerification.Fail(Name, CaptchaFailure.Rejected, "Challenge mismatch");

        if (!FixedTimeEqualsHex(HmacHex(key, payload.Challenge), payload.Signature))
            return CaptchaVerification.Fail(Name, CaptchaFailure.Rejected, "Bad signature");

        var saltParams = ParseSaltParams(payload.Salt);
        if (!saltParams.TryGetValue("expires", out var expiresText)
            || !long.TryParse(expiresText, out var expires)
            || expires <= _clock.GetUtcNow().ToUnixTimeSeconds())
        {
            return CaptchaVerification.Fail(Name, CaptchaFailure.ExpiredOrReplayed, "Challenge expired");
        }

        saltParams.TryGetValue("purpose", out var purpose);
        if (!string.Equals(purpose, request.Purpose, StringComparison.OrdinalIgnoreCase))
            return CaptchaVerification.Fail(Name, CaptchaFailure.ActionMismatch, $"purpose={purpose} expected={request.Purpose}").WithReport(null, purpose, null);

        // 一次性：把 challenge 记到过期为止。TrySet 是原子的「不存在才写」，两次并发提交只有一次能过。
        var ttl = TimeSpan.FromSeconds(expires - _clock.GetUtcNow().ToUnixTimeSeconds() + 1);
        var fresh = await _cache.TrySetAsync(ReplayKey(payload.Challenge), true, ttl, cancellationToken);
        if (!fresh)
            return CaptchaVerification.Fail(Name, CaptchaFailure.ExpiredOrReplayed, "Replayed");

        return CaptchaVerification.Pass(Name).WithReport(null, purpose, null);
    }

    /// <inheritdoc />
    public CaptchaClientConfigDto GetClientConfig() => new()
    {
        Enabled = true,
        Provider = Name,
        ScriptUrl = _scriptUrl,
        ChallengeUrl = ChallengePath
    };

    private byte[] RequireKey()
    {
        if (string.IsNullOrWhiteSpace(_options.HmacKey))
        {
            // 校验器在启动期已拦下；这里是配置在运行期被清空的兜底，宁可抛也不能出一道谁都能签的题。
            _logger.LogError("Altcha HmacKey is not configured.");
            throw new ConfigurationException("AspNetCore:Captcha:Altcha:HmacKey is required when the altcha provider is active.");
        }
        return Encoding.UTF8.GetBytes(_options.HmacKey);
    }

    private static string ReplayKey(string challenge) => $"captcha:altcha:used:{challenge}";

    private static string Sha256Hex(string input)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();

    private static string HmacHex(byte[] key, string input)
        => Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(input))).ToLowerInvariant();

    private static bool FixedTimeEqualsHex(string expectedHex, string actualHex)
    {
        if (actualHex.Length != expectedHex.Length) return false;
        byte[] expected, actual;
        try
        {
            expected = Convert.FromHexString(expectedHex);
            actual = Convert.FromHexString(actualHex);
        }
        catch (FormatException)
        {
            return false;
        }
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    private static Dictionary<string, string> ParseSaltParams(string salt)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var q = salt.IndexOf('?');
        if (q < 0) return result;
        foreach (var pair in salt[(q + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq <= 0) continue;
            result[pair[..eq]] = Uri.UnescapeDataString(pair[(eq + 1)..]);
        }
        return result;
    }

    /// <summary>控件交回的载荷。</summary>
    private sealed class AltchaPayload
    {
        [JsonPropertyName("algorithm")]
        public string? Algorithm { get; set; }

        [JsonPropertyName("challenge")]
        public string? Challenge { get; set; }

        [JsonPropertyName("number")]
        public long Number { get; set; }

        [JsonPropertyName("salt")]
        public string? Salt { get; set; }

        [JsonPropertyName("signature")]
        public string? Signature { get; set; }
    }
}
