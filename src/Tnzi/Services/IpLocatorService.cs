
namespace Tnzi.Services;

/// <summary>
/// IP定位服务实现（使用本地IP库或第三方API）
/// </summary>
public class IpLocatorService : IIpLocatorService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ICache? _cache;
    private readonly ILogger<IpLocatorService>? _logger;
    private static readonly TimeSpan CacheExpiration = TimeSpan.FromHours(24);

    /// <summary>
    /// 初始化一个<see cref="IpLocatorService"/>类型的新实例
    /// </summary>
    public IpLocatorService(IHttpClientFactory httpClientFactory, ICache? cache = null, ILogger<IpLocatorService>? logger = null)
    {
        _httpClientFactory = Check.NotNull(httpClientFactory);
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// 根据IP地址获取位置信息
    /// </summary>
    public async Task<IpLocationResult?> LocateAsync(string ipAddress, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
            return null;

        // 检查本地IP
        if (IsLocalIp(ipAddress))
        {
            return new IpLocationResult
            {
                IpAddress = ipAddress,
                Country = "Local",
                City = "-"
            };
        }

        // 检查缓存
        if (_cache != null)
        {
            var cachedResult = await _cache.GetAsync<IpLocationResult>(GetCacheKey(ipAddress), cancellationToken);
            if (cachedResult != null)
            {
                return cachedResult;
            }
        }

        // 调用第三方API（示例：使用ip-api.com，免费但有限制）
        var result = await LocateFromApiAsync(ipAddress, cancellationToken);

        // 缓存结果
        if (result != null && _cache != null)
        {
            await _cache.SetAsync(GetCacheKey(ipAddress), result, CacheExpiration, cancellationToken);
        }

        return result;
    }

    /// <summary>
    /// 批量获取IP位置信息
    /// </summary>
    public async Task<Dictionary<string, IpLocationResult?>> LocateBatchAsync(IEnumerable<string> ipAddresses, CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, IpLocationResult?>();
        var tasks = ipAddresses.Select(async ip =>
        {
            var result = await LocateAsync(ip, cancellationToken);
            return new { Ip = ip, Result = result };
        });

        var completed = await Task.WhenAll(tasks);
        foreach (var item in completed)
        {
            results[item.Ip] = item.Result;
        }

        return results;
    }

    /// <summary>
    /// 从API获取IP位置信息
    /// </summary>
    private async Task<IpLocationResult?> LocateFromApiAsync(string ipAddress, CancellationToken cancellationToken = default)
    {
        try
        {
            // 使用ip-api.com免费API（限制：每分钟45次请求）
            // 注意：生产环境建议使用付费API或本地IP库
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);

            var url = $"http://ip-api.com/json/{ipAddress}";
            var response = await client.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            // ip-api.com 返回的字段是 camelCase（status/regionName/...），必须用大小写不敏感的
            // 反序列化选项，否则所有属性都绑不上，Status 恒为空、本方法恒返回 null
            var apiResult = JsonSerializer.Deserialize<IpApiResponse>(json, TnziJsonDefaults.Options);

            if (apiResult == null || apiResult.Status != "success")
                return null;

            return new IpLocationResult
            {
                IpAddress = ipAddress,
                Country = apiResult.Country,
                Province = apiResult.RegionName,
                City = apiResult.City,
                Isp = apiResult.Isp,
                Longitude = apiResult.Lon,
                Latitude = apiResult.Lat,
                FullAddress = $"{apiResult.Country} {apiResult.RegionName} {apiResult.City}"
            };
        }
        catch (HttpRequestException ex)
        {
            // HTTP 请求异常（网络错误、超时等）
            _logger?.LogWarning(ex, "Failed to locate IP address {IpAddress} due to HTTP request error", ipAddress);
            return null;
        }
        catch (TaskCanceledException ex)
        {
            // 请求超时或取消
            _logger?.LogWarning(ex, "IP location request for {IpAddress} was cancelled or timed out", ipAddress);
            return null;
        }
        catch (JsonException ex)
        {
            // JSON 解析错误
            _logger?.LogWarning(ex, "Failed to parse IP location API response for {IpAddress}", ipAddress);
            return null;
        }
        catch (Exception ex)
        {
            // 其他未预期的异常
            _logger?.LogError(ex, "Unexpected error while locating IP address {IpAddress}", ipAddress);
            return null;
        }
    }

    /// <summary>
    /// 判断是否为本地IP
    /// </summary>
    private static bool IsLocalIp(string ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
            return false;

        // 检查是否为localhost
        if (ipAddress.Equals("127.0.0.1", StringComparison.Ordinal) ||
            ipAddress.Equals("::1", StringComparison.Ordinal) ||
            ipAddress.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            return true;

        // 尝试解析IP地址
        if (!IPAddress.TryParse(ipAddress, out var address))
            return false;

        // 检查是否为内网IP
        return IsPrivateIpAddress(address);
    }

    /// <summary>
    /// 判断是否为私有IP地址
    /// </summary>
    private static bool IsPrivateIpAddress(IPAddress address)
    {
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            // IPv4 地址
            var bytes = address.GetAddressBytes();
            
            // 10.0.0.0/8
            if (bytes[0] == 10)
                return true;
            
            // 172.16.0.0/12 (172.16.0.0 - 172.31.255.255)
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                return true;
            
            // 192.168.0.0/16
            if (bytes[0] == 192 && bytes[1] == 168)
                return true;
            
            // 169.254.0.0/16 (链路本地地址)
            if (bytes[0] == 169 && bytes[1] == 254)
                return true;
        }
        else if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            // IPv6 地址
            var bytes = address.GetAddressBytes();
            
            // ::1 (localhost): 前 15 字节为 0 且末字节为 1
            // （不能写成 bytes.All(b => b == 0)，那要求末字节也为 0，与 bytes[15] == 1 互斥，分支永不成立）
            if (bytes[15] == 1 && bytes.Take(15).All(b => b == 0))
                return true;
            
            // fe80::/10 (链路本地地址)
            if (bytes[0] == 0xfe && (bytes[1] & 0xc0) == 0x80)
                return true;
            
            // fc00::/7 (唯一本地地址)
            if ((bytes[0] & 0xfe) == 0xfc)
                return true;
        }

        return false;
    }

    /// <summary>
    /// 获取缓存键
    /// </summary>
    private static string GetCacheKey(string ipAddress)
    {
        return $"IpLocation:{ipAddress}";
    }

    /// <summary>
    /// IP-API响应模型
    /// </summary>
    private class IpApiResponse
    {
        public string Status { get; set; } = string.Empty;
        public string? Country { get; set; }
        public string? RegionName { get; set; }
        public string? City { get; set; }
        public string? Isp { get; set; }
        public double? Lat { get; set; }
        public double? Lon { get; set; }
    }
}