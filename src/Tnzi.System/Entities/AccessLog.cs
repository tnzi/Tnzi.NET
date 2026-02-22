
namespace Tnzi.System.Entities;

/// <summary>
/// 访问日志实体
/// </summary>
public class AccessLog : CreationAuditedEntity<Guid>
{
    public Guid? UserId { get; set; }
    public string? UserName { get; set; }
    public string Path { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public int StatusCode { get; set; }
    public long ResponseTime { get; set; }

    // IP定位信息
    public string? IpCountry { get; set; }
    public string? IpProvince { get; set; }
    public string? IpCity { get; set; }
    public string? IpDistrict { get; set; }
    public string? IpIsp { get; set; }
    public double? IpLongitude { get; set; }
    public double? IpLatitude { get; set; }
    public string? IpFullAddress { get; set; }

    // UserAgent解析信息
    public string? UaBrowser { get; set; }
    public string? UaBrowserVersion { get; set; }
    public string? UaOperatingSystem { get; set; }
    public string? UaOperatingSystemVersion { get; set; }
    public string? UaDeviceType { get; set; }
    public string? UaDeviceBrand { get; set; }
    public string? UaDeviceModel { get; set; }
    public bool UaIsMobile { get; set; }
    public bool UaIsBot { get; set; }
}
