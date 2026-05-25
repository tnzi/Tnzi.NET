namespace Tnzi.System.Configuration;

/// <summary>
/// 从 Setting 表读取 Global 作用域配置，填入 IConfiguration data dictionary。
/// 启动时 Load() 留空（DI 未 ready），由 <see cref="ReloadFromDatabaseAsync"/> 在
/// SystemModule.OnApplicationInitializationAsync 中首次填充，之后由 SettingChangedEventHandler 触发 reload。
///
/// 仅暴露未加密配置。加密配置依旧由 ISettingService.GetDecryptedAsync 单条读取，
/// 避免明文落到内存 dict + IOptionsMonitor 缓存。
/// </summary>
public sealed class SettingConfigurationProvider : ConfigurationProvider
{
    private IServiceProvider? _rootServiceProvider;
    private readonly SemaphoreSlim _reloadLock = new(1, 1);

    internal SettingConfigurationSource OwningSource { get; }

    internal SettingConfigurationProvider(SettingConfigurationSource owningSource)
    {
        OwningSource = Check.NotNull(owningSource);
    }

    public override void Load()
    {
        // 启动时 DI 未 ready，留空。Bind 期间业务 Options 取 appsettings 值。
    }

    /// <summary>
    /// 由 SystemModule.OnApplicationInitializationAsync 注入根 ServiceProvider，
    /// 允许后续 reload 拿 scope 读 IRepository&lt;Setting&gt;。
    /// </summary>
    internal void AttachServiceProvider(IServiceProvider rootServiceProvider)
    {
        _rootServiceProvider = Check.NotNull(rootServiceProvider);
    }

    /// <summary>
    /// 从 Setting 表 Global scope 读所有非加密配置，覆盖 Data，触发 IConfiguration reload。
    /// 失败静默（用 ILogger 记录），不允许打断应用启动 / 事件处理。
    /// </summary>
    internal async Task ReloadFromDatabaseAsync(CancellationToken cancellationToken = default)
    {
        if (_rootServiceProvider == null)
            return;

        await _reloadLock.WaitAsync(cancellationToken);
        try
        {
            using var scope = _rootServiceProvider.CreateScope();
            var repository = scope.ServiceProvider.GetService<IRepository<Setting, Guid>>();
            var logger = scope.ServiceProvider.GetService<ILoggerFactory>()?.CreateLogger<SettingConfigurationProvider>();

            if (repository == null)
            {
                logger?.LogWarning("IRepository<Setting> not available; SettingConfigurationProvider skipped reload");
                return;
            }

            try
            {
                var settings = await repository.AsQueryable()
                    .AsNoTracking()
                    .Where(s => s.Scope == SettingScope.Global && !s.IsEncrypted)
                    .Select(s => new { s.Key, s.Value })
                    .ToListAsync(cancellationToken);

                var excluded = OwningSource.ExcludedKeys;
                var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                foreach (var s in settings)
                {
                    if (string.IsNullOrWhiteSpace(s.Key))
                        continue;
                    if (excluded.Contains(s.Key))
                        continue;
                    data[s.Key] = s.Value;
                }

                Data = data;
                OnReload();
                logger?.LogDebug("SettingConfigurationProvider loaded {Count} setting(s) from database", data.Count);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "SettingConfigurationProvider failed to reload from database");
            }
        }
        finally
        {
            _reloadLock.Release();
        }
    }
}
