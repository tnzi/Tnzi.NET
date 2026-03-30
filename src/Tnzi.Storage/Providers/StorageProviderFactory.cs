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

    public StorageProviderContext(StorageOptions options, IConfiguration configuration, IWebHostEnvironment? environment = null, ILoggerFactory? loggerFactory = null)
    {
        Options = Check.NotNull(options);
        Configuration = Check.NotNull(configuration);
        Environment = environment;
        LoggerFactory = loggerFactory;
    }
}

/// <summary>
/// 存储提供者工厂 — 可扩展的提供者注册表
/// <para>
/// 内置提供者: Local, S3, R2, Azure
/// 自定义提供者: 在模块的 PreConfigureServicesAsync 中调用 <see cref="Register"/> 注册
/// </para>
/// </summary>
public static class StorageProviderFactory
{
    private static readonly Dictionary<string, Func<StorageProviderContext, IFileStorage>> _providers = new(StringComparer.OrdinalIgnoreCase);

    static StorageProviderFactory()
    {
        // 注册内置提供者
        Register("local", ctx => new LocalStorage(ctx.Configuration, ctx.Environment));
        Register("s3", ctx =>
        {
            if (ctx.Options.S3 == null)
                throw new InvalidOperationException("S3 options are required when Provider is S3.");
            return new S3Storage(ctx.Options.S3, ctx.Configuration);
        });
        Register("r2", ctx =>
        {
            if (ctx.Options.R2 == null)
                throw new InvalidOperationException("R2 options are required when Provider is R2.");
            return new R2Storage(ctx.Options.R2, ctx.Configuration);
        });
        Register("inmemory", _ => new InMemoryFileStorage());
        Register("azure", ctx =>
        {
            if (ctx.Options.Azure == null)
                throw new InvalidOperationException("Azure options are required when Provider is Azure.");
            ILogger<AzureBlobStorage>? logger = ctx.LoggerFactory?.CreateLogger<AzureBlobStorage>();
            return new AzureBlobStorage(ctx.Options.Azure, ctx.Configuration, logger);
        });
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
    public static IFileStorage Create(StorageProviderContext context)
    {
        Check.NotNull(context);
        var providerName = context.Options.Provider ?? "Local";

        if (_providers.TryGetValue(providerName, out var factory))
        {
            return factory(context);
        }

        throw new InvalidOperationException(
            $"Unknown storage provider '{providerName}'. Registered providers: {string.Join(", ", _providers.Keys)}. " +
            $"Register custom providers via StorageProviderFactory.Register() in your module's PreConfigureServicesAsync.");
    }

}
