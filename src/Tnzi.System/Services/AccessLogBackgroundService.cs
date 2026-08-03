namespace Tnzi.System.Services;

/// <summary>
/// 访问日志后台处理服务
/// </summary>
public class AccessLogBackgroundService : ChannelBatchProcessorBase<AccessLogDto>
{
    public AccessLogBackgroundService(
        IAccessLogConsumer consumer,
        IServiceProvider serviceProvider,
        ILogger<AccessLogBackgroundService> logger)
        : base(Check.NotNull(consumer).Reader, serviceProvider, logger)
    {
    }

    /// <inheritdoc />
    protected override async Task ProcessBatchAsync(
        IReadOnlyList<AccessLogDto> batch,
        IServiceProvider scopedServices,
        CancellationToken cancellationToken)
    {
        var repository = scopedServices.GetRequiredService<IRepository<AccessLog, Guid>>();
        var ipLocator = scopedServices.GetService<IIpLocatorService>();
        var uaParser = scopedServices.GetService<IUserAgentParserService>();

        var entities = new List<AccessLog>(batch.Count);

        foreach (var log in batch)
        {
            var entity = log.MapTo<AccessLog>();
            await EnrichAsync(entity, log, ipLocator, uaParser, cancellationToken);
            entities.Add(entity);
        }

        await repository.InsertManyAsync(entities);
    }

    /// <summary>
    /// 在后台补齐 IP 归属地与 UserAgent 解析 —— 这两项都可能走网络/正则，放在请求链路上会拖慢响应。
    /// </summary>
    /// <remarks>
    /// 富化失败只降级为"少几个字段"，不能让整批日志写不进去：归属地库不可用是常态，
    /// 而访问日志本身（路径 / 状态码 / 耗时）才是主要价值。
    /// </remarks>
    private async Task EnrichAsync(
        AccessLog entity,
        AccessLogDto log,
        IIpLocatorService? ipLocator,
        IUserAgentParserService? uaParser,
        CancellationToken cancellationToken)
    {
        try
        {
            if (ipLocator != null && !string.IsNullOrEmpty(log.IpAddress))
            {
                var location = await ipLocator.LocateAsync(log.IpAddress, cancellationToken);
                if (location != null)
                {
                    entity.IpCountry = location.Country;
                    entity.IpProvince = location.Province;
                    entity.IpCity = location.City;
                    entity.IpDistrict = location.District;
                    entity.IpIsp = location.Isp;
                    entity.IpLongitude = location.Longitude;
                    entity.IpLatitude = location.Latitude;
                    entity.IpFullAddress = location.FullAddress;
                }
            }

            if (uaParser != null && !string.IsNullOrEmpty(log.UserAgent))
            {
                var uaInfo = uaParser.Parse(log.UserAgent);
                if (uaInfo != null)
                {
                    entity.UaBrowser = uaInfo.Browser;
                    entity.UaBrowserVersion = uaInfo.BrowserVersion;
                    entity.UaOperatingSystem = uaInfo.OperatingSystem;
                    entity.UaOperatingSystemVersion = uaInfo.OperatingSystemVersion;
                    entity.UaDeviceType = uaInfo.DeviceType;
                    entity.UaDeviceBrand = uaInfo.DeviceBrand;
                    entity.UaDeviceModel = uaInfo.DeviceModel;
                    entity.UaIsMobile = uaInfo.IsMobile;
                    entity.UaIsBot = uaInfo.IsBot;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogWarning(ex, "Failed to parse IP or UA for access log.");
        }
    }
}
