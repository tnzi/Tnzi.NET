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
/// 访问日志查询参数
/// </summary>
public class AccessLogQueryDto : PagedQueryDto
{
    public Guid? UserId { get; set; }
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
}
