namespace Tnzi.AspNetCore.Options;

/// <summary>
/// 请求验证配置选项
/// </summary>
public class RequestValidationOptions
{
    /// <summary>
    /// 获取或设置 是否启用请求验证
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// 获取或设置 时间戳验证窗口（秒），默认 300 秒（5 分钟）
    /// 请求时间戳与服务器时间的差值不能超过此值
    /// </summary>
    public int TimestampWindowSeconds { get; set; } = 300;

    /// <summary>
    /// 获取或设置 是否要求请求签名
    /// </summary>
    public bool RequireSignature { get; set; } = false;

    /// <summary>
    /// 获取或设置 签名密钥（用于 HMAC-SHA256 签名验证）
    /// </summary>
    public string? SignatureSecretKey { get; set; }

    /// <summary>
    /// 获取或设置 是否要求 Nonce 防重放
    /// </summary>
    public bool RequireNonce { get; set; } = false;

    /// <summary>
    /// 获取或设置 Nonce 过期时间（秒），默认 600 秒（10 分钟）
    /// </summary>
    public int NonceExpirationSeconds { get; set; } = 600;

    /// <summary>
    /// 获取或设置 需要验证的路径列表（为空则验证所有路径）
    /// </summary>
    public string[]? IncludePaths { get; set; }

    /// <summary>
    /// 获取或设置 排除验证的路径列表
    /// </summary>
    public string[]? ExcludePaths { get; set; }
}

