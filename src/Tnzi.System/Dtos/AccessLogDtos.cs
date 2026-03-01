namespace Tnzi.System.Dtos;

/// <summary>
/// 访问日志输出
/// </summary>
public class AccessLogDto
{
    public Guid Id { get; set; }
    public Guid? UserId { get; set; }
    public string? UserName { get; set; }
    public string Path { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public int StatusCode { get; set; }
    public long ResponseTime { get; set; }
    public DateTime CreationTime { get; set; }
    public string? IpCountry { get; set; }
    public string? IpProvince { get; set; }
    public string? IpCity { get; set; }
    public string? IpFullAddress { get; set; }
    public string? UaBrowser { get; set; }
    public string? UaOperatingSystem { get; set; }
    public string? UaDeviceType { get; set; }
    public bool UaIsMobile { get; set; }
}

/// <summary>
/// 访问日志统计
/// </summary>
public class AccessLogStatisticsDto
{
    public int TotalRequests { get; set; }
    public int UniqueUsers { get; set; }
    public int SuccessRequests { get; set; }
    public int ErrorRequests { get; set; }
    public double AverageResponseTime { get; set; }
}

/// <summary>
/// 访问日志趋势时间间隔
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AccessLogTrendInterval
{
    /// <summary>
    /// 按天
    /// </summary>
    Daily = 0,

    /// <summary>
    /// 按周
    /// </summary>
    Weekly = 1,

    /// <summary>
    /// 按月
    /// </summary>
    Monthly = 2
}

/// <summary>
/// 访问日志趋势数据点
/// </summary>
public class AccessLogTrendDataPoint
{
    /// <summary>
    /// 时间标签 (e.g., "2026-02-28", "2026-W09", "2026-02")
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// 时间段起始时间
    /// </summary>
    public DateTime StartTime { get; set; }

    /// <summary>
    /// 请求总数
    /// </summary>
    public int TotalRequests { get; set; }

    /// <summary>
    /// 成功请求数 (2xx)
    /// </summary>
    public int SuccessRequests { get; set; }

    /// <summary>
    /// 错误请求数 (4xx+5xx)
    /// </summary>
    public int ErrorRequests { get; set; }

    /// <summary>
    /// 独立用户数
    /// </summary>
    public int UniqueUsers { get; set; }

    /// <summary>
    /// 平均响应时间 (ms)
    /// </summary>
    public double AverageResponseTime { get; set; }
}

/// <summary>
/// 访问日志趋势统计
/// </summary>
public class AccessLogTrendDto
{
    /// <summary>
    /// 时间间隔
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AccessLogTrendInterval Interval { get; set; }

    /// <summary>
    /// 起始时间
    /// </summary>
    public DateTime StartDate { get; set; }

    /// <summary>
    /// 结束时间
    /// </summary>
    public DateTime EndDate { get; set; }

    /// <summary>
    /// 数据点列表
    /// </summary>
    public List<AccessLogTrendDataPoint> DataPoints { get; set; } = new();
}

/// <summary>
/// Top 端点排序方式
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TopEndpointSortBy
{
    /// <summary>
    /// 按请求数排序（最热端点）
    /// </summary>
    Hits = 0,

    /// <summary>
    /// 按平均响应时间排序（最慢端点）
    /// </summary>
    AverageResponseTime = 1,

    /// <summary>
    /// 按错误数排序（最多错误端点）
    /// </summary>
    Errors = 2
}

/// <summary>
/// Top 端点统计 DTO
/// </summary>
public class TopEndpointDto
{
    /// <summary>
    /// 请求路径
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// HTTP 方法
    /// </summary>
    public string Method { get; set; } = string.Empty;

    /// <summary>
    /// 请求总数
    /// </summary>
    public int TotalHits { get; set; }

    /// <summary>
    /// 成功请求数 (2xx)
    /// </summary>
    public int SuccessHits { get; set; }

    /// <summary>
    /// 错误请求数 (4xx+5xx)
    /// </summary>
    public int ErrorHits { get; set; }

    /// <summary>
    /// 平均响应时间 (ms)
    /// </summary>
    public double AverageResponseTime { get; set; }

    /// <summary>
    /// 最大响应时间 (ms)
    /// </summary>
    public long MaxResponseTime { get; set; }

    /// <summary>
    /// 错误率
    /// </summary>
    public double ErrorRate { get; set; }
}

/// <summary>
/// 访问日志查询参数
/// </summary>
public class AccessLogQueryDto : PagedQueryDto
{
    public Guid? UserId { get; set; }
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }

    /// <summary>
    /// 按请求路径过滤（模糊匹配）
    /// </summary>
    public string? Path { get; set; }

    /// <summary>
    /// 按 HTTP 方法过滤（GET/POST/PUT/DELETE 等）
    /// </summary>
    public string? Method { get; set; }

    /// <summary>
    /// 按状态码过滤（精确匹配）
    /// </summary>
    public int? StatusCode { get; set; }

    /// <summary>
    /// 按最小状态码过滤（范围查询）
    /// </summary>
    public int? MinStatusCode { get; set; }

    /// <summary>
    /// 按最大状态码过滤（范围查询）
    /// </summary>
    public int? MaxStatusCode { get; set; }

    /// <summary>
    /// 按 IP 地址过滤（模糊匹配）
    /// </summary>
    public string? IpAddress { get; set; }

    /// <summary>
    /// 关键词搜索（匹配 Path 或 UserName）
    /// </summary>
    public string? Keyword { get; set; }
}
