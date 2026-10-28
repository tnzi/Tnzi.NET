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
    /// </summary>
    /// <param name="propagateFailure">
    /// 读库失败时是否抛出。启动期首次填充（<see cref="SettingConfigurationSource.AttachAsync"/>）传 <see langword="false"/>：
    /// 失败只记日志、沿用 appsettings 的值，不打断启动。变更后的重载（<see cref="SettingConfigurationSource.ReloadAsync"/>）
    /// 传 <see langword="true"/>：失败必须让调用方（配置变更事件的处理器）知道，事件总线据此重试。
    /// 此前一律只记一条 Warning，一次瞬时的数据库故障就让 <c>IOptionsMonitor</c> 停在旧值，直到有人再改任意一个设置或重启，
    /// 而事件总线以为处理成功、不会重试。失败时 <see cref="ConfigurationProvider.Data"/> 保持原样（上一次成功读到的值）。
    /// </param>
    /// <param name="cancellationToken">取消令牌。</param>
    internal async Task ReloadFromDatabaseAsync(bool propagateFailure, CancellationToken cancellationToken = default)
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
                    if (IsExcluded(s.Key, excluded))
                        continue;
                    data[s.Key] = s.Value;
                }

                Data = data;
                OnReload();
                logger?.LogDebug("SettingConfigurationProvider loaded {Count} setting(s) from database", data.Count);
            }
            catch (Exception ex) when (!propagateFailure)
            {
                logger?.LogWarning(ex, "SettingConfigurationProvider failed to load from database; the configuration keeps its previous values");
            }
        }
        finally
        {
            _reloadLock.Release();
        }
    }

    /// <summary>
    /// 一个键是否被排除：等于某个排除项，或落在某个排除项下面的整棵子树里（IConfiguration 的节语义，不分大小写）。
    /// </summary>
    /// <remarks>
    /// 排除项可以写成节名（<c>Appearance:Theme</c>）或带结尾冒号的前缀（<c>Appearance:Theme:</c>），两者都挡住
    /// <c>Appearance:Theme:admin</c>，都不挡 <c>Appearance:ThemeColor</c>。此前只做精确匹配，而文档让宿主
    /// 「排除 <c>Appearance:Theme:</c> 前缀」：没有任何键等于这个字符串，排除不生效，也没有任何提示。
    /// </remarks>
    internal static bool IsExcluded(string key, IReadOnlySet<string> excluded)
    {
        if (excluded.Count == 0)
            return false;
        if (excluded.Contains(key))
            return true;

        foreach (var entry in excluded)
        {
            var section = entry.TrimEnd(':');
            if (section.Length == 0)
                continue;
            if (string.Equals(key, section, StringComparison.OrdinalIgnoreCase))
                return true;
            if (key.Length > section.Length
                && key[section.Length] == ':'
                && key.StartsWith(section, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
