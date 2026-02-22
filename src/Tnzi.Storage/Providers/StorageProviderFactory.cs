namespace Tnzi.Storage.Providers;

/// <summary>
/// 存储提供者工厂
/// </summary>
public static class StorageProviderFactory
{
    /// <summary>
    /// 创建存储提供者实例
    /// </summary>
    /// <param name="providerName">提供者名称（Local, S3, R2, Azure）</param>
    /// <param name="options">文件存储配置选项</param>
    /// <param name="configuration">配置</param>
    /// <param name="environment">Web宿主环境（Local存储需要）</param>
    /// <param name="loggerFactory">日志工厂（Azure存储需要）</param>
    /// <returns>存储提供者实例</returns>
    public static IFileStorage Create(
        string providerName,
        StorageOptions options,
        IConfiguration configuration,
        IWebHostEnvironment? environment = null,
        ILoggerFactory? loggerFactory = null)
    {
        if (string.IsNullOrEmpty(providerName))
            providerName = "Local";

        return providerName.ToLowerInvariant() switch
        {
            "local" => CreateLocalStorage(configuration, environment),
            "s3" => CreateS3Storage(options, configuration),
            "r2" => CreateR2Storage(options, configuration),
            "azure" => CreateAzureStorage(options, configuration, loggerFactory),
            _ => CreateLocalStorage(configuration, environment)
        };
    }

    /// <summary>
    /// 创建本地存储实例
    /// </summary>
    private static IFileStorage CreateLocalStorage(IConfiguration configuration, IWebHostEnvironment? environment)
    {
        return new LocalStorage(configuration, environment);
    }

    /// <summary>
    /// 创建 S3 存储实例
    /// </summary>
    private static IFileStorage CreateS3Storage(StorageOptions options, IConfiguration configuration)
    {
        if (options.S3 == null)
            throw new InvalidOperationException("S3 options are required when Provider is S3.");

        return new S3Storage(options.S3, configuration);
    }

    /// <summary>
    /// 创建 R2 存储实例（R2 兼容 S3 API）。
    /// </summary>
    private static IFileStorage CreateR2Storage(StorageOptions options, IConfiguration configuration)
    {
        if (options.R2 == null)
            throw new InvalidOperationException("R2 options are required when Provider is R2.");

        return new R2Storage(options.R2, configuration);
    }

    /// <summary>
    /// 创建 Azure 存储实例
    /// </summary>
    private static IFileStorage CreateAzureStorage(
        StorageOptions options,
        IConfiguration configuration,
        ILoggerFactory? loggerFactory)
    {
        if (options.Azure == null)
            throw new InvalidOperationException("Azure options are required when Provider is Azure.");

        ILogger<AzureBlobStorage>? logger = null;
        if (loggerFactory != null)
        {
            logger = loggerFactory.CreateLogger<AzureBlobStorage>();
        }

        return new AzureBlobStorage(options.Azure, configuration, logger);
    }
}
