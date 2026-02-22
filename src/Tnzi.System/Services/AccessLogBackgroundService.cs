namespace Tnzi.System.Services;

/// <summary>
/// 访问日志后台处理服务
/// </summary>
public class AccessLogBackgroundService : BackgroundService
{
    private readonly IAccessLogConsumer _consumer;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AccessLogBackgroundService> _logger;

    public AccessLogBackgroundService(
        IAccessLogConsumer consumer,
        IServiceProvider serviceProvider,
        ILogger<AccessLogBackgroundService> logger)
    {
        _consumer = Check.NotNull(consumer);
        _serviceProvider = Check.NotNull(serviceProvider);
        _logger = Check.NotNull(logger);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Access log background service is starting.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // 等待数据可用
                if (await _consumer.Reader.WaitToReadAsync(stoppingToken))
                {
                    await ProcessBatchAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                // 正常停止
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in access log background service.");
                await Task.Delay(5000, stoppingToken);
            }
        }

        _logger.LogInformation("Access log background service is stopping.");
    }

    private async Task ProcessBatchAsync(CancellationToken stoppingToken)
    {
        var logs = new List<AccessLogDto>();

        // 读取一个批次，最多100条
        while (logs.Count < 100 && _consumer.Reader.TryRead(out var log))
        {
            logs.Add(log);
        }

        if (logs.Count == 0) return;

        using var scope = _serviceProvider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<AccessLog, Guid>>();
        var ipLocator = scope.ServiceProvider.GetService<IIpLocatorService>();
        var uaParser = scope.ServiceProvider.GetService<IUserAgentParserService>();

        var entities = new List<AccessLog>();

        foreach (var log in logs)
        {
            var entity = log.MapTo<AccessLog>();
            entity.CreationTime = DateTime.UtcNow;

            // 在后台进行 IP 定位和 UA 解析，不阻塞主流程
            try
            {
                // 处理IP定位
                if (ipLocator != null && !string.IsNullOrEmpty(log.IpAddress))
                {
                    var location = await ipLocator.LocateAsync(log.IpAddress, stoppingToken);
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

                // 处理UserAgent解析
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
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse IP or UA for access log.");
            }

            entities.Add(entity);
        }

        // 批量写入数据库
        try
        {
            await repository.InsertManyAsync(entities);
            _logger.LogDebug("Saved batch of {Count} access logs.", entities.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save batch of access logs to database.");
        }
    }
}
