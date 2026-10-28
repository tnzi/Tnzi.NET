
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

    /// <summary>
    /// 请求所属租户：采集时取自调用者的身份（宿主、匿名请求、未开多租户时为 null）。
    /// </summary>
    /// <remarks>
    /// 刻意不实现 <c>IMultiTenant</c>：后台服务在没有租户上下文的作用域里落库，全局租户过滤器会把写入挡住或改写。
    /// 读取面按调用者的租户自己加谓词（见 <c>AccessLogService</c>）。多租户关闭时该列被 Ignore（见 <c>AccessLogConfiguration</c>）。
    /// </remarks>
    public Guid? TenantId { get; set; }

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
