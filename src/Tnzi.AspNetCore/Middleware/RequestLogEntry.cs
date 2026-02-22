
namespace Tnzi.AspNetCore.Middleware;

/// <summary>
/// 请求日志条目
/// </summary>
public class RequestLogEntry
{
    /// <summary>
    /// 请求 ID
    /// </summary>
    public string RequestId { get; init; } = "";

    /// <summary>
    /// 请求时间
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// HTTP 方法
    /// </summary>
    public string Method { get; init; } = "";

    /// <summary>
    /// 请求路径
    /// </summary>
    public string Path { get; init; } = "";

    /// <summary>
    /// 查询字符串
    /// </summary>
    public string? QueryString { get; init; }

    /// <summary>
    /// IP 地址
    /// </summary>
    public string? IpAddress { get; init; }

    /// <summary>
    /// User Agent
    /// </summary>
    public string? UserAgent { get; init; }

    /// <summary>
    /// 请求体
    /// </summary>
    public string? RequestBody { get; init; }

    /// <summary>
    /// 响应状态码
    /// </summary>
    public int StatusCode { get; set; }

    /// <summary>
    /// 响应时间（毫秒）
    /// </summary>
    public double DurationMs { get; set; }

    /// <summary>
    /// 是否慢请求
    /// </summary>
    public bool IsSlow { get; set; }

    /// <summary>
    /// 用户 ID（如果已认证）
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// 响应体
    /// </summary>
    public string? ResponseBody { get; set; }

    /// <summary>
    /// 转换为 JSON
    /// </summary>
    public string ToJson()
    {
        return JsonSerializer.Serialize(this);
    }
}