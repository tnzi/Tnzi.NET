
namespace Tnzi.Services;

/// <summary>
/// 第三方 IP 定位实现：把公网 IP 发给 ip-api.com 的免费接口查询归属地。
/// </summary>
/// <remarks>
/// <para>
/// ★ <strong>框架不默认注册它</strong>，消费方要显式注册（<c>services.AddScoped&lt;IIpLocatorService, IpLocatorService&gt;()</c>）。
/// 启用即意味着：每个被定位的公网地址以<b>明文 HTTP</b>（该免费接口不提供 HTTPS）交给一个第三方处理；
/// 免费接口限每分钟 45 次请求、条款限非商业用途。生产部署建议注册自己的实现（本地 IP 库或签约的商业接口）。
/// </para>
/// <para>
/// 只对合法的公网 IP 外呼：解析不出 IP 的值直接返回 <c>null</c>，回环 / 内网 / 链路本地地址就地答 <c>Local</c>。
/// 成功结果缓存 24 小时；失败结果缓存 10 分钟：外呼被拦或被限流时，同一个地址不会让每条访问日志都再等一次超时。
/// </para>
/// </remarks>
public class IpLocatorService : IIpLocatorService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ICache? _cache;
    private readonly ILogger<IpLocatorService>? _logger;
    private static readonly TimeSpan CacheExpiration = TimeSpan.FromHours(24);
    private static readonly TimeSpan FailureCacheExpiration = TimeSpan.FromMinutes(10);
    private const string FailureMarker = "miss";

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

        var trimmed = ipAddress.Trim();
        if (trimmed.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            return LocalResult(ipAddress);

        // 不是 IP 的值一律不外呼：拼进 URL 的只能是解析后的规范形式
        if (!IPAddress.TryParse(trimmed, out var address))
            return null;

        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (IsLocalAddress(address))
            return LocalResult(ipAddress);

        var canonical = address.ToString();

        if (_cache != null)
        {
            var cachedResult = await _cache.GetAsync<IpLocationResult>(GetCacheKey(canonical), cancellationToken);
            if (cachedResult != null)
            {
                return cachedResult;
            }

            if (await _cache.GetAsync<string>(GetFailureCacheKey(canonical), cancellationToken) != null)
            {
                return null;
            }
        }

        var result = await LocateFromApiAsync(canonical, cancellationToken);

        if (_cache != null)
        {
            if (result != null)
            {
                await _cache.SetAsync(GetCacheKey(canonical), result, CacheExpiration, cancellationToken);
            }
            else if (!cancellationToken.IsCancellationRequested)
            {
                // 调用方取消（如停机）不是这个地址查不到，不记失败
                await _cache.SetAsync(GetFailureCacheKey(canonical), FailureMarker, FailureCacheExpiration, cancellationToken);
            }
        }

        return result;
    }

    private static IpLocationResult LocalResult(string ipAddress) => new()
    {
        IpAddress = ipAddress,
        Country = "Local",
        City = "-"
    };

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
            // ip-api.com 免费接口：明文 HTTP、每分钟 45 次、限非商业用途（见类注释）
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
    /// 判断是否为不该外呼的本地地址（回环、内网、链路本地）
    /// </summary>
    private static bool IsLocalAddress(IPAddress address)
        => IPAddress.IsLoopback(address) || IsPrivateIpAddress(address);

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
    /// 获取失败结果的缓存键
    /// </summary>
    private static string GetFailureCacheKey(string ipAddress)
    {
        return $"IpLocation:Failed:{ipAddress}";
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