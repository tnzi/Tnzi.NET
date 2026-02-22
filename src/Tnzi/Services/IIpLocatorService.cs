
namespace Tnzi.Services;

/// <summary>
/// IP定位服务接口
/// </summary>
public interface IIpLocatorService
{
    /// <summary>
    /// 根据IP地址获取位置信息
    /// </summary>
    /// <param name="ipAddress">IP地址</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>位置信息</returns>
    Task<IpLocationResult?> LocateAsync(string ipAddress, CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量获取IP位置信息
    /// </summary>
    /// <param name="ipAddresses">IP地址列表</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>位置信息字典（IP地址 -> 位置信息）</returns>
    Task<Dictionary<string, IpLocationResult?>> LocateBatchAsync(IEnumerable<string> ipAddresses, CancellationToken cancellationToken = default);
}

/// <summary>
/// IP位置信息结果
/// </summary>
public class IpLocationResult
{
    /// <summary>
    /// 获取或设置 IP地址
    /// </summary>
    public string IpAddress { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 国家
    /// </summary>
    public string? Country { get; set; }

    /// <summary>
    /// 获取或设置 省份/州
    /// </summary>
    public string? Province { get; set; }

    /// <summary>
    /// 获取或设置 城市
    /// </summary>
    public string? City { get; set; }

    /// <summary>
    /// 获取或设置 区县
    /// </summary>
    public string? District { get; set; }

    /// <summary>
    /// 获取或设置 ISP（互联网服务提供商）
    /// </summary>
    public string? Isp { get; set; }

    /// <summary>
    /// 获取或设置 经度
    /// </summary>
    public double? Longitude { get; set; }

    /// <summary>
    /// 获取或设置 纬度
    /// </summary>
    public double? Latitude { get; set; }

    /// <summary>
    /// 获取或设置 完整地址描述
    /// </summary>
    public string? FullAddress { get; set; }
}