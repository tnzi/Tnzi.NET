namespace Tnzi.Storage.Providers;

/// <summary>
/// Storage provider creation context
/// </summary>
public class StorageProviderContext
{
    public StorageOptions Options { get; }
    public IConfiguration Configuration { get; }
    public IWebHostEnvironment? Environment { get; }
    public ILoggerFactory? LoggerFactory { get; }

    /// <summary>
    /// 选项监视器，使单例 provider 能在运行时热读取 UrlPrefix（可选）
    /// </summary>
    public IOptionsMonitor<StorageOptions>? OptionsMonitor { get; }

    public StorageProviderContext(StorageOptions options, IConfiguration configuration, IWebHostEnvironment? environment = null, ILoggerFactory? loggerFactory = null, IOptionsMonitor<StorageOptions>? optionsMonitor = null)
    {
        Options = Check.NotNull(options);
        Configuration = Check.NotNull(configuration);
        Environment = environment;
        LoggerFactory = loggerFactory;
        OptionsMonitor = optionsMonitor;
    }
}

/// <summary>
/// 存储提供者工厂 - 可扩展的提供者注册表
/// <para>
/// 本模块自带: Local, InMemory
/// </para>
/// <para>
/// 对象存储 (S3, R2, Azure): 由可选子模块 <c>Tnzi.Storage.Cloud</c> 在它的
/// PreConfigureServicesAsync 里注册，名字与拆分前一致。<c>AWSSDK.S3</c> 与
/// <c>Azure.Storage.Blobs</c> 住在那个包里，本模块不引用它们。
/// </para>
/// <para>
/// 自定义提供者: 在模块的 PreConfigureServicesAsync 中调用 <see cref="Register"/> 注册
/// </para>
/// </summary>
public static class StorageProviderFactory
{
    private static readonly Dictionary<string, Func<StorageProviderContext, IFileStorage>> _providers = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 住在可选子模块 <c>Tnzi.Storage.Cloud</c> 里的 provider 名字。
    /// 只用于把「没加载那个包」和「名字打错了」这两种解析失败区分开来，
    /// 让前者的报错能直接说出该加载什么，而不是只丢一句「未知的 provider」。
    /// </summary>
    private static readonly string[] _cloudProviderNames = ["s3", "r2", "azure"];

    static StorageProviderFactory()
    {
        // 注册内置提供者
        Register("local", ctx => new LocalStorage(ctx.Configuration, ctx.Environment, optionsMonitor: ctx.OptionsMonitor));
        Register("inmemory", _ => new InMemoryFileStorage());
    }

    /// <summary>
    /// Register a custom storage provider.
    /// Call in your module's PreConfigureServicesAsync to add custom providers.
    /// </summary>
    /// <param name="providerName">Provider name (case-insensitive)</param>
    /// <param name="factory">Factory function to create the provider</param>
    public static void Register(string providerName, Func<StorageProviderContext, IFileStorage> factory)
    {
        Check.NotNullOrWhiteSpace(providerName);
        Check.NotNull(factory);
        _providers[providerName] = factory;
    }

    /// <summary>
    /// Check if a provider is registered
    /// </summary>
    public static bool IsRegistered(string providerName)
    {
        return _providers.ContainsKey(providerName);
    }

    /// <summary>
    /// Get all registered provider names
    /// </summary>
    public static IReadOnlyCollection<string> GetRegisteredProviders()
    {
        return _providers.Keys.ToList().AsReadOnly();
    }

    /// <summary>
    /// 创建存储提供者实例
    /// </summary>
    /// <remarks>
    /// 解析不到名字时<b>抛异常</b>，绝不回退到 Local。回退看起来更宽容，实际是把
    /// 「本该进对象存储的文件」默默写到本地磁盘上，而配置、日志、接口返回全都正常，
    /// 等到发现时本地盘上已经堆了一批没人预期的文件、且没有任何一条记录指出它们为什么在那里。
    /// </remarks>
    public static IFileStorage Create(StorageProviderContext context)
    {
        Check.NotNull(context);
        var providerName = context.Options.Provider ?? "Local";

        if (_providers.TryGetValue(providerName, out var factory))
        {
            return factory(context);
        }

        // 名字是三家对象存储之一 = 配置没写错，是少加载了那个包。报错要直接说这句话，
        // 否则拿到的只有一句「未知的 provider」，而配置文件看上去完全正确。
        var hint = _cloudProviderNames.Contains(providerName, StringComparer.OrdinalIgnoreCase)
            ? $"'{providerName}' is provided by the optional Tnzi.Storage.Cloud module; load it "
              + "([DependsOn(typeof(StorageCloudModule))]) to use object storage. "
            : string.Empty;

        throw new InvalidOperationException(
            $"Unknown storage provider '{providerName}'. Registered providers: {string.Join(", ", _providers.Keys)}. " +
            hint +
            $"Register custom providers via StorageProviderFactory.Register() in your module's PreConfigureServicesAsync.");
    }

}
