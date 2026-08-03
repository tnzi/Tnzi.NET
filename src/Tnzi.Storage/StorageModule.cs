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
        // 注册配置选项（统一入口：section 由 [ConfigSection("Storage")] 解析 + Bind + 验证器）
        context.Services.AddTnziOptions<StorageOptions, StorageOptionsValidator>(context.Configuration);

        return Task.CompletedTask;
    }

    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // Code-declared permissions for this module's admin surfaces - the
        // Authorization module's PermissionDbSeeder picks every registered
        // provider up on startup (no-op when Authorization is not loaded).
        context.Services.AddTransient<IPermissionDefinitionProvider, StoragePermissions>();

        var services = context.Services;

        // 请求体大小上限：放开到配置的 MaxFileSize，让"最大文件"真正生效。
        // 否则 Kestrel 默认 30MB（IIS 同样有默认上限）会在 FileStorageService 的
        // MaxFileSize 校验之前就把更大的请求体以不透明的 413 拒绝，使配置的上限
        // （默认 100MB）形同虚设。多租户/分片上传不受影响（分片走小请求）。
        var maxFileSize = context.Configuration.GetValue<long?>("Storage:MaxFileSize") ?? (100L * 1024 * 1024);
        var bodyLimit = maxFileSize + (1L * 1024 * 1024); // 预留 multipart 边界/表单字段的余量
        services.Configure<KestrelServerOptions>(o => o.Limits.MaxRequestBodySize = bodyLimit);
        services.Configure<IISServerOptions>(o => o.MaxRequestBodySize = bodyLimit);
        services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = bodyLimit);

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
            var optionsMonitor = provider.GetService<IOptionsMonitor<StorageOptions>>();

            var ctx = new StorageProviderContext(options, configuration, environment, loggerFactory, optionsMonitor);
            return StorageProviderFactory.Create(ctx);
        });

        // 文件访问策略。TryAdd:消费方可注册自己的实现整体替换默认的
        // "归属 + 权限码 + 显式公开" 策略。
        services.TryAddScoped<IFileAccessAuthorizer, FileAccessAuthorizer>();

        // 访问令牌签名器(单例:密钥构造时解析一次)。让私密文件能被 <img src> 渲染 ——
        // 浏览器发起的资源请求带不了 Authorization 头。
        services.TryAddSingleton<IFileUrlSigner, FileUrlSigner>();

        // 请求作用域的授予表:分享链接校验通过后把结论放进这里,授权器据此放行。
        // 判定因此仍然全在服务层,而不是散到控制器上。
        services.TryAddScoped<IFileAccessGrantContext, FileAccessGrantContext>();

        // 判定要读当前请求的查询参数(`?sig=`),故需要 HttpContext。
        // 幂等:AspNetCore 模块通常已注册。
        services.AddHttpContextAccessor();

        // [FileField(Public = true)] 字段清单（反射结果缓存，故为单例）。
        // 供历史数据回填用：字段声明只对之后写入的引用生效。
        services.TryAddSingleton<IPublicFileFieldResolver, PublicFileFieldResolver>();

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

        // 注册默认孤立引用验证器（用 TryAdd 以便应用覆盖）
        // 通过 IEntityManager 解析实体类型并按主键查询存在性，找不到/不确定时保守返回 true（绝不误删）
        services.TryAddScoped<IOrphanReferenceValidator, EntityManagerOrphanReferenceValidator>();

        // 注册文件清理服务
        services.AddScoped<IFileCleanupService, FileCleanupService>();

        // 注册文件清理后台任务（定时执行）
        services.AddHostedService<FileCleanupBackgroundService>();

        return Task.CompletedTask;
    }
}
