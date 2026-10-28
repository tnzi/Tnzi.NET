namespace Tnzi.System.Services;

/// <summary>
/// 访问日志后台处理服务
/// </summary>
public class AccessLogBackgroundService : ChannelBatchProcessorBase<AccessLogDto>
{
    private readonly IServiceProvider _serviceProvider;

    public AccessLogBackgroundService(
        IAccessLogConsumer consumer,
        IServiceProvider serviceProvider,
        ILogger<AccessLogBackgroundService> logger)
        : base(Check.NotNull(consumer).Reader, serviceProvider, logger)
    {
        _serviceProvider = Check.NotNull(serviceProvider);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// ★ 批量 INSERT 是一条语句：一行的列宽 / 约束问题会让 SQL Server / PostgreSQL 拒绝整条语句，
    /// 而基类的既定取舍是「批处理失败记日志后丢弃这一批」。两条规则叠在一起，一行坏数据就带走了
    /// 同一批里其他所有人的访问日志（默认一批 100 条）。所以两道防线：
    /// ① 落库前把每个有列宽的字段收进列宽（<see cref="AccessLogColumns"/>），队列上带来的主键一律不认；
    /// ② 仍被拒绝（<see cref="DbUpdateException"/>）时<b>逐行重试</b>，每行一个干净的 DI 作用域
    /// （失败的那个 DbContext 仍跟踪着整批实体，复用它会把上一行的失败一起重放），只有真正坏的那一行被记录并放弃。
    /// </para>
    /// <para>
    /// 其它异常（连接断开、超时）原样抛给基类：那类故障逐行重试只会把一次失败放大成一百次，
    /// 基类的记日志、退避、丢弃正是为它设计的。
    /// </para>
    /// </remarks>
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
            // 主键由落库时生成：手工上报的入口收的是完整 DTO，一个重复或已存在的 id 会让整批 AddRange / INSERT 失败。
            entity.Id = Guid.Empty;
            await EnrichAsync(entity, log, ipLocator, uaParser, cancellationToken);
            FitToColumns(entity);
            entities.Add(entity);
        }

        try
        {
            await repository.InsertManyAsync(entities, cancellationToken);
            return;
        }
        catch (DbUpdateException ex)
        {
            Logger.LogWarning(ex,
                "{Service}: the database rejected a batch of {Count} access logs; retrying row by row so that one bad row does not drop the others.",
                ServiceName, entities.Count);
        }

        await SaveRowByRowAsync(entities, cancellationToken);
    }

    private async Task SaveRowByRowAsync(IReadOnlyList<AccessLog> entities, CancellationToken cancellationToken)
    {
        var dropped = 0;
        foreach (var entity in entities)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var scope = _serviceProvider.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IRepository<AccessLog, Guid>>();
            try
            {
                await repository.InsertAsync(entity, cancellationToken);
            }
            catch (DbUpdateException ex)
            {
                dropped++;
                // 记下足够认出这一行的信息，但不记它的内容：让它失败的往往就是内容本身。
                Logger.LogError(ex,
                    "{Service}: dropped one access log the database refused ({Method}, path length {PathLength}, user {UserId}, user agent length {UserAgentLength}).",
                    ServiceName, entity.Method, entity.Path?.Length ?? 0, entity.UserId, entity.UserAgent?.Length ?? 0);
            }
        }

        if (dropped > 0)
        {
            Logger.LogWarning("{Service}: row-by-row retry finished; {Saved} of {Count} access logs saved, {Dropped} dropped.",
                ServiceName, entities.Count - dropped, entities.Count, dropped);
        }
    }

    /// <summary>
    /// 把每个有列宽的字段收进列宽。
    /// </summary>
    /// <remarks>
    /// 富化出来的字段长度不受任何约束（UA 解析器原样交出请求头里的版本号与设备型号，归属地库是第三方数据），
    /// 路径与方法则可能来自手工上报的入口而不是中间件。所以在落库这一个汇合点统一收口，而不是指望每个生产者各自截断。
    /// 必填列（路径、方法）为空时补空串：NOT NULL 列上的一个 null 同样会让整条 INSERT 被拒。
    /// </remarks>
    private static void FitToColumns(AccessLog entity)
    {
        entity.Path = AccessLogColumns.Fit(entity.Path ?? string.Empty, AccessLogColumns.PathMaxLength)!;
        entity.Method = AccessLogColumns.Fit(entity.Method ?? string.Empty, AccessLogColumns.MethodMaxLength)!;
        entity.IpAddress = AccessLogColumns.Fit(entity.IpAddress, AccessLogColumns.IpAddressMaxLength);
        entity.IpCountry = AccessLogColumns.Fit(entity.IpCountry, AccessLogColumns.IpCountryMaxLength);
        entity.IpProvince = AccessLogColumns.Fit(entity.IpProvince, AccessLogColumns.IpProvinceMaxLength);
        entity.IpCity = AccessLogColumns.Fit(entity.IpCity, AccessLogColumns.IpCityMaxLength);
        entity.IpDistrict = AccessLogColumns.Fit(entity.IpDistrict, AccessLogColumns.IpDistrictMaxLength);
        entity.IpIsp = AccessLogColumns.Fit(entity.IpIsp, AccessLogColumns.IpIspMaxLength);
        entity.IpFullAddress = AccessLogColumns.Fit(entity.IpFullAddress, AccessLogColumns.IpFullAddressMaxLength);
        entity.UaBrowser = AccessLogColumns.Fit(entity.UaBrowser, AccessLogColumns.UaBrowserMaxLength);
        entity.UaBrowserVersion = AccessLogColumns.Fit(entity.UaBrowserVersion, AccessLogColumns.UaBrowserVersionMaxLength);
        entity.UaOperatingSystem = AccessLogColumns.Fit(entity.UaOperatingSystem, AccessLogColumns.UaOperatingSystemMaxLength);
        entity.UaOperatingSystemVersion = AccessLogColumns.Fit(entity.UaOperatingSystemVersion, AccessLogColumns.UaOperatingSystemVersionMaxLength);
        entity.UaDeviceType = AccessLogColumns.Fit(entity.UaDeviceType, AccessLogColumns.UaDeviceTypeMaxLength);
        entity.UaDeviceBrand = AccessLogColumns.Fit(entity.UaDeviceBrand, AccessLogColumns.UaDeviceBrandMaxLength);
        entity.UaDeviceModel = AccessLogColumns.Fit(entity.UaDeviceModel, AccessLogColumns.UaDeviceModelMaxLength);
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
