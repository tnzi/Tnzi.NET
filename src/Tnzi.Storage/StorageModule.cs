namespace Tnzi.Storage;

/// <summary>
/// 存储模块
/// 配置路径：Storage
/// </summary>
[DependsOn(typeof(EFCore.EFCoreModule))]
public class StorageModule : TnziApplicationModule
{
    /// <summary>
    /// 存储模块加载顺序
    /// </summary>
    public override int LoadOrder => 30;

    /// <summary>
    /// 表名前缀
    /// </summary>
    public override string? TableNamePrefix => "Storage";

    public override Task PreConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // 注册配置选项
        context.Services.Configure<StorageOptions>(context.Configuration.GetSection("Storage"));

        // 注册配置验证器
        context.Services.AddSingleton<IValidateOptions<StorageOptions>, StorageOptionsValidator>();

        return Task.CompletedTask;
    }

    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        var services = context.Services;

        // 注册配置中心分组定义
        services.AddSingleton<ISettingDefinitionProvider, StorageSettingDefinitionProvider>();

        // 注册存储服务（默认使用 LocalStorage）
        // 用户可以通过配置选择不同的存储提供者（Local, S3, R2, Azure 等）
        // 自定义提供者: 在模块的 PreConfigureServicesAsync 中调用 StorageProviderFactory.Register()
        services.AddSingleton<IFileStorage>(provider =>
        {
            var options = provider.GetService<IOptions<StorageOptions>>()?.Value
                ?? throw new InvalidOperationException("StorageOptions is not configured.");
            var configuration = provider.GetRequiredService<IConfiguration>();
            var environment = provider.GetService<IWebHostEnvironment>();
            var loggerFactory = provider.GetService<ILoggerFactory>();

            var ctx = new StorageProviderContext(options, configuration, environment, loggerFactory);
            return StorageProviderFactory.Create(ctx);
        });

        // 注册文件存储服务
        services.AddScoped<IFileStorageService, FileStorageService>();

        // 注册文件目录服务
        services.AddScoped<IFileFolderService, FileFolderService>();

        // 注册拆分后的专职服务
        services.AddScoped<IFileReferenceService, FileReferenceService>();
        services.AddScoped<IFileShareService, FileShareService>();
        services.AddScoped<IFileVersionService, FileVersionService>();
        services.AddScoped<IFileChunkUploadService, FileChunkUploadService>();

        // 注册文件预览服务
        services.AddScoped<IFilePreviewService, FilePreviewService>();

        // 注册文件删除事件处理器
        // 用于在事务成功后异步删除物理文件
        services.AddEventHandler<FileDeleteRequestedEvent, FileDeleteRequestedEventHandler>();

        // 注册文件引用处理器
        // 由 TnziDbContext.SaveChangesAsync() 调用，在事务中处理文件引用变更
        services.AddScoped<IFileReferenceProcessor, FileReferenceProcessor>();

        // 注册文件清理服务
        services.AddScoped<IFileCleanupService, FileCleanupService>();

        // 注册文件清理后台任务（定时执行）
        services.AddHostedService<FileCleanupBackgroundService>();

        return Task.CompletedTask;
    }
}
