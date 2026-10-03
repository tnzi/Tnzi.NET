namespace Tnzi.AspNetCore.Security;

/// <summary>
/// 「siteverify 协议」提供商的服务端适配器：reCAPTCHA v2 / v3、hCaptcha、Turnstile 共用这一份代码，
/// 差别全部在 <see cref="SiteVerifyCaptchaDescriptor"/> 里。
/// </summary>
/// <remarks>
/// <para>
/// 协议：<c>POST verifyUrl</c>，表单编码 <c>secret</c> + <c>response</c> + <c>remoteip</c>
/// （hCaptcha 另接受 <c>sitekey</c>）；响应 JSON <c>success</c> / <c>hostname</c> / <c>error-codes</c>，
/// v3 与 Turnstile 另有 <c>action</c>，v3（及 hCaptcha 企业版）另有 <c>score</c>。
/// </para>
/// <para>
/// 三家对「同一枚令牌校验两次」都回 <c>timeout-or-duplicate</c>，一次性由验证服务保证，这里不另存已用令牌；
/// 自校验型的 Altcha 就没有这条保证，所以它自己存。
/// </para>
/// </remarks>
public class SiteVerifyCaptchaProvider : ICaptchaProvider
{
    /// <summary>调用验证服务所用的命名 HttpClient。</summary>
    public const string HttpClientName = "Tnzi.Captcha.SiteVerify";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SiteVerifyCaptchaDescriptor _descriptor;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly CaptchaVerifierOptions _options;
    private readonly ILogger<SiteVerifyCaptchaProvider> _logger;

    /// <summary>
    /// 初始化适配器。
    /// </summary>
    public SiteVerifyCaptchaProvider(
        SiteVerifyCaptchaDescriptor descriptor,
        IHttpClientFactory httpClientFactory,
        IOptionsSnapshot<CaptchaVerifierOptions> options,
        ILogger<SiteVerifyCaptchaProvider> logger)
    {
        _descriptor = Check.NotNull(descriptor);
        _httpClientFactory = Check.NotNull(httpClientFactory);
        _options = Check.NotNull(options).Value;
        _logger = Check.NotNull(logger);
    }

    /// <inheritdoc />
    public string Name => _descriptor.Name;

    /// <inheritdoc />
    public async Task<CaptchaVerification> VerifyAsync(CaptchaVerificationRequest request, CancellationToken cancellationToken = default)
    {
        Check.NotNull(request);

        if (string.IsNullOrWhiteSpace(_options.SecretKey))
        {
            // 校验器在启动期已经拦下没配密钥的托管型提供商；这里是「配置在运行期被清空」的兜底。
            _logger.LogError("Captcha provider {Provider} has no SecretKey configured; rejecting the request.", Name);
            return CaptchaVerification.Fail(Name, CaptchaFailure.Rejected, "SecretKey is not configured.");
        }

        var form = new List<KeyValuePair<string, string>>
        {
            new("secret", _options.SecretKey),
            new("response", request.Token)
        };
        if (!string.IsNullOrWhiteSpace(request.RemoteIp))
            form.Add(new("remoteip", request.RemoteIp));
        if (_descriptor.SendsSiteKey && !string.IsNullOrWhiteSpace(_options.SiteKey))
            form.Add(new("sitekey", _options.SiteKey));

        var verifyUrl = string.IsNullOrWhiteSpace(_options.VerifyUrl) ? _descriptor.VerifyUrl : _options.VerifyUrl;

        SiteVerifyResponse? payload;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var content = new FormUrlEncodedContent(form);
            using var response = await client.PostAsync(verifyUrl, content, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                var status = (int)response.StatusCode;
                if (IsTransientStatus(response.StatusCode))
                {
                    _logger.LogWarning("Captcha provider {Provider} verify endpoint answered HTTP {StatusCode}.", Name, status);
                    return CaptchaVerification.Fail(Name, CaptchaFailure.VerifierUnavailable, $"HTTP {status}");
                }

                // 其余非 2xx（4xx、3xx）说的是「这个请求本身不对」：地址配错、参数被拒、令牌畸形。
                // 它不是服务宕机，不能落进 OnVerifierUnavailable=Allow 的放行口 —— 否则一枚构造出来
                // 让验证服务回 400 的令牌就等于通行证。
                _logger.LogError("Captcha provider {Provider} verify endpoint rejected the request with HTTP {StatusCode}; check VerifyUrl and keys.", Name, status);
                return CaptchaVerification.Fail(Name, CaptchaFailure.Rejected, $"HTTP {status}");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            payload = await JsonSerializer.DeserializeAsync<SiteVerifyResponse>(stream, JsonOptions, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Captcha provider {Provider} verify endpoint timed out after {Timeout}s.", Name, _options.TimeoutSeconds);
            return CaptchaVerification.Fail(Name, CaptchaFailure.VerifierUnavailable, "Timeout");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException)
        {
            _logger.LogWarning(ex, "Captcha provider {Provider} verify endpoint could not be reached or answered garbage.", Name);
            return CaptchaVerification.Fail(Name, CaptchaFailure.VerifierUnavailable, ex.Message);
        }

        if (payload == null)
            return CaptchaVerification.Fail(Name, CaptchaFailure.VerifierUnavailable, "Empty response");

        var errorCodes = payload.ErrorCodes ?? [];
        if (!payload.Success)
        {
            var detail = errorCodes.Length > 0 ? string.Join(",", errorCodes) : "success=false";

            // 密钥类错误不是「用户答错」，是部署配错：每一枚令牌都会被拒，必须让运维看见。
            if (errorCodes.Any(c => c.Contains("secret", StringComparison.OrdinalIgnoreCase)))
                _logger.LogError("Captcha provider {Provider} rejected the server secret: {ErrorCodes}", Name, detail);

            var failure = errorCodes.Contains("timeout-or-duplicate", StringComparer.OrdinalIgnoreCase)
                ? CaptchaFailure.ExpiredOrReplayed
                : CaptchaFailure.Rejected;
            return CaptchaVerification.Fail(Name, failure, detail);
        }

        var verification = new SiteVerifyOutcome(payload.Hostname, _descriptor.ReportsAction ? payload.Action : null, _descriptor.ReportsScore ? payload.Score : null);
        return ApplyPolicies(request, verification);
    }

    /// <summary>
    /// 只有这些状态码算「验证服务暂时不可用」：5xx、408（超时）、429（限流）。
    /// </summary>
    internal static bool IsTransientStatus(HttpStatusCode statusCode)
    {
        var code = (int)statusCode;
        return code >= 500 || statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests;
    }

    private CaptchaVerification ApplyPolicies(CaptchaVerificationRequest request, SiteVerifyOutcome outcome)
    {
        if (_options.EnforceAction && outcome.Action != null
            && !string.Equals(FoldAction(outcome.Action), FoldAction(request.Purpose), StringComparison.OrdinalIgnoreCase))
        {
            return Fail(CaptchaFailure.ActionMismatch, $"action={outcome.Action} purpose={request.Purpose}", outcome);
        }

        if (_options.ExpectedHostnames is { Length: > 0 } && outcome.Hostname != null
            && !_options.ExpectedHostnames.Contains(outcome.Hostname, StringComparer.OrdinalIgnoreCase))
        {
            return Fail(CaptchaFailure.HostnameMismatch, $"hostname={outcome.Hostname}", outcome);
        }

        if (outcome.Score.HasValue && outcome.Score.Value < _options.ScoreThreshold)
        {
            return Fail(CaptchaFailure.LowScore, $"score={outcome.Score.Value:0.00} threshold={_options.ScoreThreshold:0.00}", outcome);
        }

        return CaptchaVerification.Pass(Name).WithReport(outcome.Hostname, outcome.Action, outcome.Score);
    }

    private CaptchaVerification Fail(CaptchaFailure failure, string detail, SiteVerifyOutcome outcome)
        => CaptchaVerification.Fail(Name, failure, detail).WithReport(outcome.Hostname, outcome.Action, outcome.Score);

    /// <summary>
    /// reCAPTCHA v3 的 action 只接受 <c>[A-Za-z0-9/_]</c>，客户端把用途里的连字符折成下划线再提交
    /// （<c>password-recovery</c> → <c>password_recovery</c>）；比对时两边同样折叠，Turnstile 原样带连字符也不受影响。
    /// </summary>
    private static string FoldAction(string action) => action.Replace('-', '_');

    /// <inheritdoc />
    public CaptchaClientConfigDto GetClientConfig()
    {
        var script = string.IsNullOrWhiteSpace(_options.ScriptUrl) ? _descriptor.ScriptUrl : _options.ScriptUrl;
        return new CaptchaClientConfigDto
        {
            Enabled = true,
            Provider = Name,
            SiteKey = _options.SiteKey,
            ScriptUrl = script.Replace("{siteKey}", _options.SiteKey ?? string.Empty, StringComparison.Ordinal)
        };
    }

    private sealed record SiteVerifyOutcome(string? Hostname, string? Action, double? Score);

    /// <summary>siteverify 响应体。</summary>
    private sealed class SiteVerifyResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("hostname")]
        public string? Hostname { get; set; }

        [JsonPropertyName("action")]
        public string? Action { get; set; }

        [JsonPropertyName("score")]
        public double? Score { get; set; }

        [JsonPropertyName("error-codes")]
        public string[]? ErrorCodes { get; set; }
    }
}
