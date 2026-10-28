
namespace Tnzi.AspNetCore.Security;

/// <summary>
/// 请求验证器实现
/// 支持时间戳验证、签名验证和 Nonce 防重放
/// </summary>
public class RequestValidator : IRequestValidator
{
    private readonly RequestValidationOptions _options;
    private readonly ICache _cache;
    private readonly ILogger<RequestValidator> _logger;

    /// <summary>
    /// 初始化一个<see cref="RequestValidator"/>类型的新实例
    /// </summary>
    public RequestValidator(
        IOptions<AspNetCoreOptions> aspNetCoreOptions,
        ICache cache,
        ILogger<RequestValidator> logger)
    {
        _options = Check.NotNull(aspNetCoreOptions).Value.RequestValidation ?? new RequestValidationOptions();
        _cache = Check.NotNull(cache);
        _logger = Check.NotNull(logger);
    }

    /// <summary>
    /// 验证请求
    /// </summary>
    public async Task<string?> ValidateAsync(HttpContext context)
    {
        // 检查是否启用验证
        if (!_options.Enabled)
            return null;

        // 检查路径是否需要验证
        if (!ShouldValidatePath(context.Request.Path))
            return null;

        // 验证时间戳（所有验证都需要时间戳）
        if (!ValidateTimestamp(context))
        {
            return "Request timestamp validation failed. The request may be expired or the time is incorrect.";
        }

        // Nonce 先只查形态（有没有、长不长），不写缓存
        string? nonce = null;
        if (_options.RequireNonce)
        {
            var nonceError = ReadNonce(context, out nonce);
            if (nonceError != null)
                return nonceError;
        }

        // 验证签名（签名串里已经含 Nonce，签名对了 Nonce 才可信）
        if (_options.RequireSignature)
        {
            var signatureError = await ValidateSignatureAsync(context);
            if (signatureError != null)
                return signatureError;
        }

        // 最后才登记 Nonce：本校验在认证与限流之前，先写缓存再验签意味着每个带随机 Nonce、签名乱填的未认证请求
        // 都在缓存里留下一条至少 TTL 秒的记录，缓存随攻击者的带宽无界增长（配了容量上限时还会挤掉正常的 Nonce，重开重放窗口）
        if (nonce != null)
        {
            var replayError = await RecordNonceAsync(nonce);
            if (replayError != null)
                return replayError;
        }

        return null;
    }

    /// <summary>
    /// X-Nonce 允许的最大长度。Nonce 会整条成为缓存键，不设上限等于让调用方决定每个键多大。
    /// </summary>
    public const int MaxNonceLength = 128;

    /// <summary>
    /// 检查路径是否需要验证
    /// </summary>
    private bool ShouldValidatePath(PathString path)
    {
        var pathValue = path.Value ?? string.Empty;

        // 检查排除路径
        if (_options.ExcludePaths != null && _options.ExcludePaths.Length > 0)
        {
            foreach (var excludePath in _options.ExcludePaths)
            {
                if (pathValue.StartsWith(excludePath, StringComparison.OrdinalIgnoreCase))
                    return false;
            }
        }

        // 检查包含路径
        if (_options.IncludePaths != null && _options.IncludePaths.Length > 0)
        {
            foreach (var includePath in _options.IncludePaths)
            {
                if (pathValue.StartsWith(includePath, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        return true;
    }

    /// <summary>
    /// 验证时间戳
    /// </summary>
    private bool ValidateTimestamp(HttpContext context)
    {
        var timestampHeader = context.Request.Headers["X-Timestamp"].FirstOrDefault();
        if (string.IsNullOrEmpty(timestampHeader))
        {
            _logger.LogWarning("Request missing X-Timestamp header");
            return false;
        }

        if (!long.TryParse(timestampHeader, out var timestamp))
        {
            _logger.LogWarning("Invalid X-Timestamp format: {Timestamp}", timestampHeader);
            return false;
        }

        // 统一使用 DateTimeOffset 进行时间比较
        var requestTime = DateTimeOffset.FromUnixTimeSeconds(timestamp);
        var serverTime = DateTimeOffset.UtcNow;
        var timeDiff = Math.Abs((serverTime - requestTime).TotalSeconds);

        if (timeDiff > _options.TimestampWindowSeconds)
        {
            _logger.LogWarning(
                "Request timestamp out of window. RequestTime: {RequestTime}, ServerTime: {ServerTime}, Diff: {Diff} seconds",
                requestTime, serverTime, timeDiff);
            return false;
        }

        return true;
    }

    /// <summary>
    /// 验证签名
    /// </summary>
    private async Task<string?> ValidateSignatureAsync(HttpContext context)
    {
        if (string.IsNullOrEmpty(_options.SignatureSecretKey))
        {
            _logger.LogError("Signature validation is enabled but SignatureSecretKey is not configured");
            return "Signature validation is not properly configured.";
        }

        var signatureHeader = context.Request.Headers["X-Signature"].FirstOrDefault();
        if (string.IsNullOrEmpty(signatureHeader))
        {
            return "Request missing X-Signature header";
        }

        // 构建待签名字符串
        var timestamp = context.Request.Headers["X-Timestamp"].FirstOrDefault() ?? string.Empty;
        var nonce = context.Request.Headers["X-Nonce"].FirstOrDefault() ?? string.Empty;
        var method = context.Request.Method;
        var path = context.Request.Path.Value ?? string.Empty;
        var queryString = context.Request.QueryString.Value ?? string.Empty;

        // 读取请求体（如果存在且需要）。这段读发生在比对签名之前，任何未认证调用方都能让它跑：
        // 有 Content-Length 的按声明值在读之前拒绝；没有的（chunked）只读到上限 + 1 字节就停，
        // 绝不「先读到底再查大小」—— 那等于让调用方决定服务端分配多大的字符串。
        string body = string.Empty;
        var contentLength = context.Request.ContentLength;
        const int maxBodySize = 1024 * 1024; // 1MB

        if (contentLength > maxBodySize)
        {
            _logger.LogWarning("Request body too large for signature validation: {Size} bytes", contentLength);
            return "Request body too large for signature validation";
        }

        // ContentLength > 0 或 ContentLength 为 null（chunked 传输）时读取 body
        if (contentLength is null or > 0)
        {
            var bounded = await context.Request.TryReadAsStringAsync(maxBodySize, context.RequestAborted);
            if (bounded == null)
            {
                _logger.LogWarning("Request body too large for signature validation (chunked): over {Limit} bytes", maxBodySize);
                return "Request body too large for signature validation";
            }

            body = bounded;
        }

        // 构建签名字符串：timestamp + nonce + method + path + queryString + body
        var signString = $"{timestamp}{nonce}{method}{path}{queryString}{body}";

        // 计算签名
        var expectedSignature = ComputeHmacSha256(signString, _options.SignatureSecretKey);

        // 比较签名（使用时间安全的比较）
        if (!TimeSafeCompare(signatureHeader, expectedSignature))
        {
            _logger.LogWarning("Request signature validation failed");
            return "Request signature validation failed";
        }

        return null;
    }

    /// <summary>
    /// 读出 X-Nonce 并检查形态（存在、不超过 <see cref="MaxNonceLength"/>），不碰缓存。
    /// </summary>
    private string? ReadNonce(HttpContext context, out string? nonce)
    {
        nonce = context.Request.Headers["X-Nonce"].FirstOrDefault();
        if (string.IsNullOrEmpty(nonce))
        {
            return "Request missing X-Nonce header";
        }

        if (nonce.Length > MaxNonceLength)
        {
            _logger.LogWarning("Request X-Nonce too long: {Length} characters", nonce.Length);
            nonce = null;
            return $"Request X-Nonce header must not exceed {MaxNonceLength} characters";
        }

        return null;
    }

    /// <summary>
    /// 登记 Nonce；已经用过则判为重放
    /// </summary>
    private async Task<string?> RecordNonceAsync(string nonceHeader)
    {
        var cacheKey = $"RequestNonce:{nonceHeader}";

        // 使用原子操作尝试设置 Nonce。
        // TTL 不得短于时间戳窗口的两倍（一条请求在其时间戳前后各 W 秒内都合法）：
        // 启动校验已经挡住这种配置，这里再兜一次底，绕过校验的实例也开不出重放窗口。
        var ttlSeconds = Math.Max(_options.NonceExpirationSeconds, 2L * _options.TimestampWindowSeconds);
        var success = await _cache.TrySetAsync(cacheKey, "1", TimeSpan.FromSeconds(ttlSeconds));
        
        if (!success)
        {
            _logger.LogWarning("Request nonce already used: {Nonce}", nonceHeader);
            return "Request nonce has already been used (replay attack detected)";
        }
        
        return null;
    }

    /// <summary>
    /// 计算 HMAC-SHA256 签名
    /// </summary>
    private static string ComputeHmacSha256(string data, string secretKey)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secretKey));
        var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// 时间安全的字符串比较（防止时序攻击）
    /// 使用 CryptographicOperations.FixedTimeEquals 确保常数时间比较
    /// </summary>
    private static bool TimeSafeCompare(string a, string b)
    {
        var aBytes = Encoding.UTF8.GetBytes(a);
        var bBytes = Encoding.UTF8.GetBytes(b);
        return CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
    }
}