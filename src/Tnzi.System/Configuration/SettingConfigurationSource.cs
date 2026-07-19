namespace Tnzi.System.Configuration;

/// <summary>
/// 把 Setting 表（Global 作用域）暴露成 ASP.NET Core IConfiguration source，
/// 配合 IOptionsMonitor 让业务模块的 Options 在运行时随 Setting 表更新自动 reload。
///
/// 注册时机：host build 之前（DI 尚未 ready，Provider 的 Load() 留空）。SystemModule 在
/// PreConfigureServicesAsync 中自动注册（宿主配置为 ConfigurationManager 时）；也可在
/// <c>builder.Build()</c> 之前手调 <c>builder.Configuration.AddTnziSettings()</c>（幂等）。
/// 等 SystemModule.OnApplicationInitializationAsync 拿到 IServiceProvider 后再首次填充并允许 reload。
///
/// 同一进程仅有一个 Source 实例（singleton），Module 通过 DI 拿到同一引用以触发 reload。
/// </summary>
public sealed class SettingConfigurationSource : IConfigurationSource
{
    internal SettingConfigurationProvider? Provider { get; private set; }

    /// <summary>
    /// 永不进 IConfiguration 的 key 集合（OrdinalIgnoreCase）。
    /// 适合保护启动级关键开关（如测试模式标志），避免 DB 误改影响线上。
    /// </summary>
    public IReadOnlySet<string> ExcludedKeys { get; init; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        // builder.Build 会对每个 source 调一次 Build()。为保证 reload 拿到同一实例，缓存第一次创建的 provider。
        Provider ??= new SettingConfigurationProvider(this);
        return Provider;
    }

    /// <summary>
    /// 由 SystemModule.OnApplicationInitializationAsync 调用：注入根 IServiceProvider 并
    /// 触发首次从数据库拉取配置。后续 reload 通过 <see cref="ReloadAsync"/> 重入。
    /// </summary>
    public async Task AttachAsync(IServiceProvider rootServiceProvider, CancellationToken cancellationToken = default)
    {
        Check.NotNull(rootServiceProvider);
        if (Provider == null)
            return;    // Source 未 Build (理论上 host build 后一定有), no-op
        Provider.AttachServiceProvider(rootServiceProvider);
        await Provider.ReloadFromDatabaseAsync(cancellationToken);
    }

    /// <summary>
    /// 重新从数据库拉取 Setting 表内容，触发 IConfiguration reload token 让 IOptionsMonitor 通知订阅者。
    /// 必须在 <see cref="AttachAsync"/> 之后调用；之前调用会静默 no-op。
    /// </summary>
    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        if (Provider == null)
            return;
        await Provider.ReloadFromDatabaseAsync(cancellationToken);
    }
}
