namespace Tnzi.Storage;

/// <summary>
/// 存储模块：程序化的对象仓库 —— 别的模块往里写字节、按 id 取回字节、靠 <c>[FileField]</c>
/// 数引用、到期回收。
/// 配置路径：Storage
/// </summary>
/// <remarks>
/// 「人在界面上拿文件做的事」（目录树 / 分享链接 / 版本 / 断点续传）在可选子模块
/// <c>Tnzi.Storage.Workspace</c>，五张表随它走。本模块声明其中三个契约
/// （<see cref="IFileShareService"/> / <see cref="IFileVersionService"/> /
/// <see cref="IFileChunkUploadService"/>）并在两个默认控制器上可选注入它们，
/// 因为那批端点长在本模块的 <c>files</c> / <c>admin/files</c> 路由上、没有搬走。
/// </remarks>
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

        // 反方向的那一问：业务模块把一个文件 id 写进自己的记录之前，问一句
        // 「这个人本来就读得到它吗」。契约在核心（Tnzi/Storage/），所以 Chat 这类
        // 拥有业务记录的模块不必引用本程序集就能问。
        services.TryAddScoped<IFileReadAccessProbe, FileReadAccessProbe>();

        // 系统身份的字节读取：拿着 FileId 的后台（通知派发、定时任务）没有当前用户，
        // 走不了 IFileStorageService.GetAsync 那条「这个人读」的路。契约同样在核心。
        services.TryAddScoped<IFileContentReader, FileContentReader>();

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

        // 注册拆分后的专职服务。
        // 目录 / 分享 / 版本 / 分片上传四个实现在可选包 Tnzi.Storage.Workspace，不在这里注册。
        services.AddScoped<IFileReferenceService, FileReferenceService>();

        // 上传闸门（体积 / 扩展名白名单 + 净化管线）。收成一个 DI 服务而不是让每条写路径
        // 各 new 一个：三条把字节交给 provider 的路径（直传 / 分片完成 / 建新版本）必须过同一份，
        // 而其中两条已经搬进了子模块 —— 各自 new 就意味着两个程序集各抄一份，那正是它们
        // 当初漂开的原因。Scoped：净化器由消费方注册，生命周期未知，单例会形成 captive dependency。
        services.AddScoped<UploadGuard>();

        // 缩略图生成器：位图与 PDF 首页两条出图路径 + 「此刻画不画得出来」的判定，四条写路径与存量回填共用。
        // PDF 一侧可选注入核心契约 IPdfRasterizer（实现在可选包 Tnzi.Documents，本模块不引用它）：
        // 没加载时为 null，PDF 与此前一样没有缩略图。TryAdd：要给视频截帧或换出图规则的消费方整体替换。
        services.TryAddScoped<IFileThumbnailGenerator, FileThumbnailGenerator>();
        // PDF 渲染闸门必须是进程级单例：生成器是 Scoped，各自一个闸门就等于没有闸门。
        services.TryAddSingleton<PdfThumbnailRenderGate>();

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
