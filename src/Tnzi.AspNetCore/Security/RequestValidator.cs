
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
        _options = aspNetCoreOptions.Value.RequestValidation ?? new RequestValidationOptions();
        _cache = cache;
        _logger = logger;
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

        // 验证 Nonce（在签名验证之前，因为签名计算需要 Nonce）
        if (_options.RequireNonce)
        {
            var nonceError = await ValidateNonceAsync(context);
            if (nonceError != null)
                return nonceError;
        }

        // 验证签名（需要读取请求体，放在最后以减少不必要的读取）
        if (_options.RequireSignature)
        {
            var signatureError = await ValidateSignatureAsync(context);
            if (signatureError != null)
                return signatureError;
        }

        return null;
    }

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

        // 读取请求体（如果存在且需要）
        // 注意：只读取小于 1MB 的请求体，避免性能问题
        string body = string.Empty;
        var contentLength = context.Request.ContentLength ?? 0;
        const long maxBodySize = 1024 * 1024; // 1MB
        
        if (contentLength > 0 && contentLength <= maxBodySize)
        {
            // 启用缓冲以便可以多次读取
            context.Request.EnableBuffering();
            
            if (context.Request.Body.CanSeek)
            {
                var originalPosition = context.Request.Body.Position;
                context.Request.Body.Position = 0;
                using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true);
                body = await reader.ReadToEndAsync();
                context.Request.Body.Position = originalPosition;
            }
        }
        else if (contentLength > maxBodySize)
        {
            _logger.LogWarning("Request body too large for signature validation: {Size} bytes", contentLength);
            return "Request body too large for signature validation";
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
    /// 验证 Nonce
    /// </summary>
    private async Task<string?> ValidateNonceAsync(HttpContext context)
    {
        var nonceHeader = context.Request.Headers["X-Nonce"].FirstOrDefault();
        if (string.IsNullOrEmpty(nonceHeader))
        {
            return "Request missing X-Nonce header";
        }

        var cacheKey = $"RequestNonce:{nonceHeader}";
        
        // 使用原子操作尝试设置 Nonce
        var success = await _cache.TrySetAsync(cacheKey, "1", TimeSpan.FromSeconds(_options.NonceExpirationSeconds));
        
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
    /// </summary>
    private static bool TimeSafeCompare(string a, string b)
    {
        if (a.Length != b.Length)
            return false;

        var result = 0;
        for (var i = 0; i < a.Length; i++)
        {
            result |= a[i] ^ b[i];
        }

        return result == 0;
    }
}