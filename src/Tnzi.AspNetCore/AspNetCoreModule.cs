namespace Tnzi.AspNetCore;

using Microsoft.AspNetCore.ResponseCompression;
using Tnzi.Resilience;

/// <summary>
/// Tnzi ASP.NET Core 模块
/// 包含所有基础模块依赖（EFCore、EventBus、Caching）
/// 配置路径：AspNetCore
/// </summary>
[DependsOn(
    typeof(CoreServicesModule), // 核心服务
    typeof(DependencyInjectionModule), // 自动依赖注入注册
    typeof(EFCoreModule),      // 数据访问
    typeof(EventBusModule),    // 事件总线
    typeof(CachingModule),      // 缓存
    typeof(ResilienceModule),   // 弹性策略
    typeof(MapsterModule),
    typeof(LoggingModule)
)]
public class AspNetCoreModule : TnziFrameworkModule
{
    /// <summary>
    /// 缓存欢迎页面 HTML，避免每次请求都读取嵌入资源
    /// </summary>
    private static string? _cachedWelcomePageHtml;
    private static string? _cachedWelcomePageCacheKey;
    private static readonly object _welcomePageLock = new();

    /// <summary>
    /// 框架模块默认加载顺序
    /// </summary>
    public override int LoadOrder => 0;

    public override Task PreConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // 注册配置选项并启用启动时验证
        context.Services.AddTnziOptions<AspNetCoreOptions, AspNetCoreOptionsValidator>(context.Configuration);

        // 注册租户解析器配置选项（section 路径由 [ConfigSection] 派生）
        context.Services.AddTnziOptions<TenantResolverOptions>(context.Configuration);

        // 注册异常处理选项（section 路径由 [ConfigSection] 派生；中间件通过 IOptionsMonitor<ExceptionHandlingOptions> 独立注入）
        context.Services.AddTnziOptions<ExceptionHandlingOptions>(context.Configuration);

        // 注册请求追踪选项（section 路径由 [ConfigSection] 派生；中间件通过 IOptionsMonitor<RequestTrackingOptions> 独立注入）
        context.Services.AddTnziOptions<RequestTrackingOptions>(context.Configuration);

        // 人机验证选项（AspNetCore:Captcha）。校验器只管字段形态（托管型要密钥、Altcha 要 HMAC 密钥）；
        // 「指名的提供商有没有注册」要等全部模块 Configure 完才知道，放在 OnApplicationInitialization 的 EnsureConfigured。
        context.Services.AddTnziOptions<CaptchaVerifierOptions, CaptchaVerifierOptionsValidator>(context.Configuration);

        return Task.CompletedTask;
    }

    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // Code-declared permissions for this module's admin surfaces - the
        // Authorization module's PermissionDbSeeder picks every registered
        // provider up on startup (no-op when Authorization is not loaded).
        context.Services.AddTransient<IPermissionDefinitionProvider, AspNetCorePermissions>();

        // 自动添加 Controllers 支持
        // 枚举按成员名(PascalCase)序列化是框架 wire 契约;入参仍兼容数字(JsonStringEnumConverter 默认 allowIntegerValues)
        var mvcBuilder = context.Services.AddControllers()
            .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        // 自动注册所有已加载模块的程序集到 ApplicationPartManager
        // 扫描范围：所有模块（含 Framework/Infrastructure），让各模块可提供 [DefaultController]
        var appDescriptor = context.Services.FirstOrDefault(s => s.ServiceType == typeof(ITnziApplication));
        if (appDescriptor?.ImplementationInstance is ITnziApplication app)
        {
            foreach (var module in app.Modules)
            {
                var applicationPartAssemblies = GetApplicationPartAssemblies(module.Type);
                foreach (var assembly in applicationPartAssemblies)
                {
                    mvcBuilder.AddApplicationPart(assembly);
                }
            }
        }

        context.Services.AddHttpContextAccessor();

        // 注册关闭钩子：在主机优雅关闭阶段确定性地异步执行模块 OnApplicationShutdownAsync，
        // 不再仅依赖 DI 容器释放时的 sync-over-async Dispose 兜底路径（ShutdownAsync 幂等，重复触发安全）
        context.Services.AddHostedService<TnziShutdownHostedService>();

        // 注册 Web 层工具服务（IP 定位、UserAgent 解析）
        if (!context.Services.Any(s => s.ServiceType == typeof(IIpLocatorService)))
        {
            if (!context.Services.Any(s => s.ServiceType == typeof(IHttpClientFactory)))
            {
                context.Services.AddHttpClient();
            }
            context.Services.TryAddScoped<IIpLocatorService, IpLocatorService>();
        }
        if (!context.Services.Any(s => s.ServiceType == typeof(IUserAgentParserService)))
        {
            context.Services.TryAddSingleton<IUserAgentParserService, UserAgentParserService>();
        }
        // 实时 Hub 注册表：由映射 Hub 的模块经 MapTnziHub 写入，admin shell 端点读出。
        // 无条件注册（不依赖 SignalR 模块）——没加载 SignalR 时它就是个空表，
        // 正是"本机不提供实时通道"这个前端必须知道的答案。
        context.Services.TryAddSingleton<IRealtimeHubRegistry, RealtimeHubRegistry>();

        // 跨版本能力协商。目录注册成**实例**而非类型：模块在 ConfigureServices 阶段
        // （容器尚未 build）经 context.Services.DeclareCapability(...) 写进去的必须就是
        // 端点稍后读出的那个对象。空表是合法状态，表示"本部署没有任何需要两端协商才能启用的协议特性"。
        // 客户端侧按请求解析，作用域必须是 Scoped —— 声明属于某一次请求，跨请求缓存会串台。
        context.Services.AddTnziCapabilities();
        context.Services.TryAddScoped<IClientCapabilities, HttpClientCapabilities>();

        // 替换 EFCoreModule 注册的 DesignTimeCurrentUser 为 HttpContextCurrentUser
        // 使用 RemoveAll 确保移除所有已有的 ICurrentUser 注册
        context.Services.RemoveAll<ICurrentUser>();
        context.Services.AddTransient<ICurrentUser, HttpContextCurrentUser>();

        // 注册作用域上下文
        context.Services.AddScoped<IScopedContext, ScopedContext.HttpContextScopedContext>();

        // 注册HTTP加密服务
        // IHostHttpCrypto 和 HostHttpCryptoMiddleware 使用 Scoped 生命周期，
        // 确保每个请求一个实例，避免并发竞态条件（HostHttpCryptoMiddleware 实现 IMiddleware）
        context.Services.TryAddTransient<IClientHttpCrypto, ClientHttpCrypto>();
        context.Services.TryAddScoped<IHostHttpCrypto, HostHttpCrypto>();
        context.Services.TryAddScoped<HostHttpCryptoMiddleware>();

        // 创建 Controller 激活诊断收集器（在所有 Provider 注册之前）
        var controllerDiagnostics = new ControllerActivationDiagnostics();
        context.Services.AddSingleton(controllerDiagnostics);

        // 读取并配置 AspNetCore 选项
        // ★ 这是一份直接从 IConfiguration 绑出来的独立实例，只能用于 Configure 阶段的「注册还是不注册」判断。
        //   凡是在容器建好之后才消费的对象（应用模型提供者等），必须改从 IOptions<AspNetCoreOptions> 取值：
        //   Configure / PostConfigure<AspNetCoreOptions> 作用在 options 工厂造的那个实例上，与这一份无关。
        //   ControllerFilter.ControllerPredicate 曾因此永远为 null（2026-09-12 修复）。
        var aspNetCoreOptions = context.Configuration
            .GetSection("AspNetCore")
            .Get<AspNetCoreOptions>() ?? new AspNetCoreOptions();

        // 抑制 [ApiController] 内置的 ModelStateInvalidFilter（Order -2000，先于自定义
        // ModelStateValidationFilter 短路），改由框架的 ModelStateValidationFilter 接管：
        // 内置过滤器返回 RFC7807 ProblemDetails（无 code 字段），前端 normalizeApiResult
        // 读不到 code 会误判成功；自定义过滤器返回统一 ApiResult 信封 + 真实 400 状态码。
        // 仅在启用框架全局模型验证时抑制（自定义过滤器此时接管），否则保持 ASP.NET Core 默认行为。
        if (aspNetCoreOptions.EnableGlobalModelValidation)
        {
            context.Services.Configure<ApiBehaviorOptions>(options =>
            {
                options.SuppressModelStateInvalidFilter = true;
            });
        }

        // 注册过滤器（根据配置）
        context.Services.Configure<MvcOptions>(options =>
        {
            // 注册 StringTrimModelBinder：排在内置 SimpleTypeModelBinderProvider 之前
            // （它是 string 的默认 binder，我们是它的修剪版），而不是整个列表的第 0 位 ——
            // 第 0 位会抢在 BinderType / Services / Body / Header 这些 provider 前面接管
            // 每一个 string 参数，而那些来源的值不在 ValueProvider 里，参数就永远是 null。
            // provider 自己还按绑定来源让路（见 StringTrimModelBinderProvider），
            // 两道都在：位置保证 MVC 的优先序，来源判定保证列表被别人改过后仍然正确。
            var simpleTypeIndex = options.ModelBinderProviders
                .ToList()
                .FindIndex(p => p is SimpleTypeModelBinderProvider);
            options.ModelBinderProviders.Insert(
                simpleTypeIndex >= 0 ? simpleTypeIndex : 0,
                new StringTrimModelBinderProvider());

            // 全局模型验证过滤器
            if (aspNetCoreOptions.EnableGlobalModelValidation)
            {
                options.Filters.Add<ModelStateValidationFilter>();
            }

            // API 结果自动包装过滤器
            if (aspNetCoreOptions.AutoWrapApiResult)
            {
                options.Filters.AddService<ApiResultWrapperFilter>();
            }

            // 全局工作单元过滤器（需要 IUnitOfWork 服务，如果服务不存在则不注册）
            if (aspNetCoreOptions.EnableGlobalUnitOfWork)
            {
                // UnitOfWorkFilter 需要在运行时从服务容器获取，所以使用 ServiceFilter
                // 但为了简化，我们直接注册为全局过滤器，它会在运行时从 DI 容器解析
                options.Filters.AddService<UnitOfWorkFilter>();
            }

            // 应用全局路由前缀约定
            // 注意：ApiControllerRouteProvider 已通过 IApplicationModelProvider 注册
            // 必须在 ApiBehaviorApplicationModelProvider (Order = -900) 之前执行
            if (!string.IsNullOrWhiteSpace(aspNetCoreOptions.ApiPathPrefix))
            {
                options.Conventions.Add(new RoutePrefixConvention(aspNetCoreOptions.ApiPathPrefix));
            }
        });

        // 注册 API 控制器路由提供者
        // 使用 IApplicationModelProvider 而非 IApplicationModelConvention
        // 因为 [ApiController] 的验证 (ApiBehaviorApplicationModelProvider, Order = -900)
        // 在 IApplicationModelConvention 之前执行
        if (aspNetCoreOptions.EnableAutoRouteConvention)
        {
            context.Services.AddSingleton<IApplicationModelProvider>(
                sp => new Mvc.Conventions.ApiControllerRouteProvider(
                    sp.GetRequiredService<IOptions<AspNetCoreOptions>>().Value));
        }

        // 注册默认 Controller 过滤提供者 (Order = -600)
        // 当 HostingModule 未激活 DefaultControllerEnabledMarker 时，移除所有 [DefaultController] 标记的 Controller
        context.Services.AddSingleton<IApplicationModelProvider>(
            _ => new Mvc.Conventions.DefaultControllerFilterProvider(context.Services, controllerDiagnostics));

        // 注册条件控制器提供者 (Order = -500)，用于根据依赖可用性过滤Controller
        // 支持Host模块的多个版本，根据依赖不同选择Controller的可见性
        context.Services.AddSingleton<IApplicationModelProvider>(
            _ => new Mvc.Conventions.ConditionalControllerProvider(context.Services, controllerDiagnostics));

        // 注册模块 Controller 替换提供者 (Order = -450)
        // 用户同路由 Controller 自动覆盖模块默认 [DefaultController]
        context.Services.AddSingleton<IApplicationModelProvider>(
            _ => new Mvc.Conventions.ModuleControllerReplacementProvider(controllerDiagnostics));

        // 注册配置化 Controller 过滤提供者 (Order = -400)
        // （按名称/程序集通配符禁用 Controller，以及按 [SensitiveEndpoint] 名字禁用单个端点）
        // ★ 无条件注册，且从 IOptions 取值：ControllerPredicate 是委托，配置绑不出来，唯一的设置途径是
        //   PostConfigure<AspNetCoreOptions>，那作用在 options 管线的实例上。此前这里只在配置里有
        //   AspNetCore:ControllerFilter 节时才注册，并把上面那份独立实例的 ControllerFilter 交给提供者 ——
        //   按文档写的谓词一次都不会被调用，全部控制器照常挂在路由上而没有任何症状。
        //   空选项是零成本的 no-op，应用模型提供者在容器建好之后才被解析，IOptions 在此安全。
        context.Services.AddSingleton<IApplicationModelProvider>(
            sp => new Mvc.Conventions.ConfigurationControllerFilterProvider(
                sp.GetRequiredService<IOptions<AspNetCoreOptions>>().Value.ControllerFilter,
                sp.GetService<ILoggerFactory>()));

        // 注册过滤器服务
        // API 结果包装过滤器
        context.Services.AddScoped<ApiResultWrapperFilter>();

        // 注册过滤器服务（如果工作单元过滤器启用）
        // 注意：UnitOfWorkFilter 现在支持可选标记模式，即使全局模式未启用也会注册
        // 但只有在全局模式或标记了 [UnitOfWork] 时才会实际启用事务
        context.Services.AddScoped<UnitOfWorkFilter>();

        // 注册 UnitOfWorkActionFilter（用于可选标记模式）
        context.Services.AddScoped<UnitOfWorkActionFilter>();

        // 注册 AjaxOnlyFilter（供 [ServiceFilter(typeof(AjaxOnlyFilter))] 使用）
        context.Services.AddScoped<AjaxOnlyFilter>();

        // 注册请求验证服务（如果启用）
        if (aspNetCoreOptions.RequestValidation?.Enabled == true)
        {
            context.Services.AddScoped<IRequestValidator, Security.RequestValidator>();
        }
        // 注册限流服务 - 无条件注册：RateLimitingMiddleware 在 Invoke 内按
        // IOptionsMonitor.CurrentValue.Enabled 热判断，boot 门控会让配置中心
        // 的 web-ratelimit 组在默认关闭部署下永远无法热开启。
        context.Services.TryAddScoped<IRateLimitService, Security.RateLimitService>();

        // 人机验证：验证器无条件注册（未配置 Provider 时它就是「一律放行」），内置提供商无条件注册
        // （注册的是能力，选不选由配置决定；四条 siteverify 描述符共用一个适配器 + 自托管 Altcha）。
        // Identity / Imaging 在各自模块里追加 image / sliding。
        if (!context.Services.Any(s => s.ServiceType == typeof(IHttpClientFactory)))
        {
            context.Services.AddHttpClient();
        }
        context.Services.TryAddScoped<ICaptchaVerifier, CaptchaVerifier>();
        foreach (var descriptor in SiteVerifyCaptchaDescriptor.BuiltIn)
        {
            context.Services.AddSiteVerifyCaptchaProvider(descriptor);
        }
        context.Services.AddCaptchaProvider<AltchaCaptchaProvider>();

        // 注册异常统计服务（如果启用）
        if (aspNetCoreOptions.ExceptionHandling?.EnableMetrics == true)
        {
            var exceptionHandling = aspNetCoreOptions.ExceptionHandling;
            context.Services.AddSingleton<IExceptionStatistics>(sp =>
            {
                var historySize = exceptionHandling!.ExceptionHistorySize;
                return new ExceptionStatistics(historySize);
            });
            // 注册异常统计查询服务（供 Admin API 使用）
            context.Services.AddSingleton<IExceptionStatisticsService>(sp =>
            {
                var stats = sp.GetRequiredService<IExceptionStatistics>();
                return new ExceptionStatisticsService(stats);
            });
        }

        // 注册异常处理器为 Singleton（无请求级依赖，且中间件在构造时从 root provider 解析）
        context.Services.AddSingleton<BusinessExceptionHandler>();
        context.Services.AddSingleton<InfrastructureExceptionHandler>();
        context.Services.AddSingleton<ValidationExceptionHandler>();
        context.Services.AddSingleton<DefaultExceptionHandler>();

        // 注册响应压缩服务（如果启用）
        if (aspNetCoreOptions.EnableResponseCompression)
        {
            context.Services.AddResponseCompression(options =>
            {
                options.EnableForHttps = true;
                options.Providers.Add<BrotliCompressionProvider>();
                options.Providers.Add<GzipCompressionProvider>();
            });
        }

        // 注册并配置 CORS（如果启用）
        if (aspNetCoreOptions.Cors?.Enabled == true)
        {
            var corsInitializer = new DefaultCorsInitializer();
            corsInitializer.SetConfiguration(context.Configuration);
            corsInitializer.AddCors(context.Services);
            context.Services.AddSingleton<ICorsInitializer>(corsInitializer);
        }

        return Task.CompletedTask;
    }



    public override Task OnApplicationInitializationAsync(ApplicationInitializationContext context)
    {
        var app = context.App;
        if (app != null)
        {
            var aspNetCoreOptions = context.ServiceProvider
                .GetRequiredService<IOptions<AspNetCoreOptions>>()
                .Value;

            WarnIfAnonymousRateLimitingIsIneffective(context, aspNetCoreOptions);
            CheckCaptchaConfiguration(context);

            // ===================================================================
            // 中间件注册顺序说明（从外到内）：
            // -1. PathBase        - 最优先，剥离子路径前缀（IIS 虚拟目录 / 反向代理不剥离路径场景）
            // 0. ForwardedHeaders - 确保所有后续中间件获取正确的客户端 IP 和协议
            // 1. ExceptionHandling - 捕获所有下游中间件的异常
            // 2. RequestTracking  - 请求追踪，生成 RequestId，确保异常响应也带 RequestId
            // 3. CORS             - 跨域处理，确保错误响应也包含 CORS 头
            // 4. Localization     - 本地化，确保异常消息使用正确的 Culture
            // 5. HostHttpCrypto   - HTTP 加密/解密，异常可被外层捕获
            // 6. SecurityHeaders  - 安全响应头
            // 7. RequestValidation- 请求验证
            // 8. ApiVersion       - API 版本控制
            // 9. ResponseCompression - 响应压缩
            // 10. Authentication  - 认证，之后 HttpContext.User 才有 claims
            // 10.5. 模块插入点 RequestPipelineStage.AfterAuthentication - 经 AddRequestPipelineMiddleware
            //                       登记的中间件挂在这里：用户已知，而被限流 / 未认证 / 无权限的请求
            //                       还没被短路。模块在自己的 OnApplicationInitializationAsync 里
            //                       UseMiddleware 只能追加在授权之后，那里看不见 401 / 403 / 429
            //                       （Sys_AccessLog 的采集器曾挂在那里，被拒绝的请求一条都记不到）。
            // 11. RateLimiting    - 限流，★必须在认证之后：ByUser 规则、user:{id} 分区键
            //                       与白名单里的用户 ID 都读 ICurrentUser.Id，认证前它恒为空，
            //                       三处判断对每一个请求都恒假 —— 配了 ByUser 的部署会静默
            //                       退化成按来源地址分区，而启动校验与配置中心都说它开着
            //                       （2026-09-12 修正；此前排在请求验证之前）。代价是洪水流量
            //                       先付一次凭据解析；匿名洪水仍按来源地址挡下，不受影响。
            // 11.5. TenantResolver - 租户解析（认证之后，Claims 来源可用）
            // 12. Authorization   - 授权
            // 13. SPANotFound     - SPA 404 处理（最内层）
            // ===================================================================

            // -1. PathBase（必须在所有中间件之前）
            // 适用场景：反向代理不剥离路径、IIS 虚拟目录（ANCM 已处理时幂等安全）
            // 原理：若 Path 以 PathBase 开头则剥离并追加到 HttpContext.Request.PathBase，
            //       使后续路由能正确匹配不含前缀的路由定义。
            if (!string.IsNullOrWhiteSpace(aspNetCoreOptions.PathBase))
            {
                app.UsePathBase(aspNetCoreOptions.PathBase);
            }

            // 0. ForwardedHeaders 中间件（最外层，处理代理服务器转发的协议、主机、IP 与路径前缀）
            // 必须在异常处理之前，确保所有后续中间件都能获取正确的客户端 IP 和协议。
            // ★ 这是全框架**唯一**采信转发头的地方：它按 AspNetCore:TrustedProxies 声明的
            //   受信代理从右往左消费，地址写进 Connection.RemoteIpAddress（GetClientIp() 只读那个结果，
            //   见 HttpContextExtensions.GetClientIp），前缀写进 Request.PathBase。
            //   X-Forwarded-Prefix 曾由紧跟在后面的一段自建中间件无条件采信（2026-09-12 删除）：
            //   任何直连调用方都能借它覆写部署级的 AspNetCore:PathBase，欢迎页链接、管理端 hub 路径、
            //   OAuth 回调地址一并跟着改写。现在它与 For/Proto/Host 走同一道受信判定，
            //   受信代理给的前缀**整体替换** Request.PathBase（不是追加在配置的 PathBase 之后）。
            if (aspNetCoreOptions.EnableForwardedHeaders)
            {
                app.UseForwardedHeaders(ForwardedHeadersOptionsBuilder.Build(aspNetCoreOptions));
            }

            // 1. 异常处理中间件（捕获所有下游中间件和业务逻辑的异常）
            app.UseMiddleware<ExceptionHandlingMiddleware>();

            // 2. 请求追踪中间件（自动生成 RequestId，确保异常响应也携带追踪信息）
            app.UseMiddleware<RequestTrackingMiddleware>();

            // 3. CORS（在异常处理之后，确保即使出错也能返回正确的 CORS 头）
            var corsInitializer = context.ServiceProvider.GetService<ICorsInitializer>();
            corsInitializer?.UseCors(app);

            // 4. 请求本地化中间件（在异常处理之后，以便异常消息能使用正确的 Culture）
            var localizationOptions = context.ServiceProvider.GetService<IOptions<RequestLocalizationOptions>>();
            if (localizationOptions != null)
            {
                app.UseRequestLocalization();
            }

            // 5. HTTP 通信加密中间件（在异常处理之后，加密中间件的异常可被外层捕获）
            if (aspNetCoreOptions.HttpEncrypt?.Enabled == true)
            {
                app.UseMiddleware<HostHttpCryptoMiddleware>();
            }

            // 6. 安全头部中间件 - 无条件加入管道：中间件 Invoke 内按
            // IOptionsMonitor.CurrentValue.EnableSecurityHeaders 热判断（支持配置中心热开/热关）。
            app.UseMiddleware<SecurityHeadersMiddleware>();

            // 7. 请求验证中间件
            if (aspNetCoreOptions.RequestValidation?.Enabled == true)
            {
                app.UseMiddleware<RequestValidationMiddleware>();
            }

            // 8. API 版本控制中间件（如果启用）
            if (aspNetCoreOptions.ApiVersion?.Enabled == true)
            {
                app.UseMiddleware<Versioning.ApiVersionMiddleware>();
            }

            // 9. 响应压缩中间件（如果启用）
            if (aspNetCoreOptions.EnableResponseCompression)
            {
                app.UseResponseCompression();
            }

            // 10. 认证中间件（必须在路由之前调用）
            app.UseAuthentication();

            // 10.5. 模块插入点：认证之后、限流与授权之前（见 RequestPipelineStage.AfterAuthentication）
            app.UseRequestPipelineStage(RequestPipelineStage.AfterAuthentication);

            // 11. 限流中间件（★ 必须在认证之后：按用户分区 / ByUser 规则 / 用户白名单
            // 都读 ICurrentUser.Id，而它来自 HttpContext.User 的 claims，认证没跑之前恒为空。
            // 与 ASP.NET Core 内置 UseRateLimiter 的官方指引一致：按用户分区时排在 UseAuthentication 之后。）
            // 无条件加入管道：Invoke 内按 CurrentValue.Enabled 热判断。
            app.UseMiddleware<RateLimitingMiddleware>();

            // 11.5. 租户解析中间件（在认证之后，以便 Claims 来源可用）
            var tenantResolverOptions = context.ServiceProvider
                .GetRequiredService<IOptions<TenantResolverOptions>>().Value;
            var multiTenancyOptions = context.ServiceProvider
                .GetService<IOptions<MultiTenancyOptions>>()?.Value ?? new MultiTenancyOptions();
            if (multiTenancyOptions.Enabled && tenantResolverOptions.Enabled)
            {
                app.UseMiddleware<TenantResolverMiddleware>();
            }

            // 12. 授权中间件
            app.UseAuthorization();

            // 13. SPA 404 处理中间件（最内层，可选）
            if (aspNetCoreOptions.EnableSPANotFoundHandler)
            {
                app.UseMiddleware<SPANotFoundMiddleware>();
            }
        }

        // 配置默认路由（使用 WebApplication）
        var webApp = context.WebApp;
        if (webApp != null)
        {
            ConfigureDefaultRoutes(webApp);
        }

        // Flush controller activation diagnostics to logger
        var diagnostics = context.ServiceProvider.GetService<ControllerActivationDiagnostics>();
        if (diagnostics != null)
        {
            var logger = context.ServiceProvider.GetRequiredService<ILoggerFactory>()
                .CreateLogger<ControllerActivationDiagnostics>();
            diagnostics.FlushToLogger(logger);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 启动期自检：限流唯一必然静默失效的配置组合。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 三者叠加时，限流对<strong>每一个匿名请求</strong>都不生效，而配置里 <c>RateLimit:Enabled</c> 写着 true：
    /// ①关闭了来源地址采集（<see cref="AspNetCoreOptions.CollectClientIpAddress"/>）
    /// ②没有注册任何 <see cref="IRateLimitPartitionKeyProvider"/>
    /// ③取不到分区键时的处置是 <see cref="MissingPartitionKeyBehavior.Allow"/>。
    /// 三者各自都是合法且常见的选择，<strong>只有叠在一起才失效</strong>，所以没有任何单项配置校验能发现它。
    /// </para>
    /// <para>
    /// <strong>为什么不能只靠中间件那条 Warning。</strong>那条要等第一个匿名请求打进来才出现，
    /// 且每个请求一条、淹在请求日志里。启动时说一次，说的是另一件事：
    /// 这个组合不是「限流开着」，是「限流对匿名请求关着」。
    /// </para>
    /// <para>
    /// <strong>刻意只告警不改变行为。</strong>默认值的兼容承诺不能因为一次自检而破掉
    /// （同 <see cref="MissingPartitionKeyBehavior.Allow"/> 保留为默认值的理由）：
    /// 启动即失败会让一批既有部署升级后起不来，而它们并没有配错任何东西。
    /// </para>
    /// </remarks>
    private static void WarnIfAnonymousRateLimitingIsIneffective(
        ApplicationInitializationContext context,
        AspNetCoreOptions options)
    {
        var rateLimit = options.RateLimit;

        // 限流本来就没开：不存在「以为它开着」的误解，无需告警。
        if (rateLimit is not { Enabled: true })
        {
            return;
        }

        // 任一条不成立，匿名请求就还有分区维度或还会被拒绝。
        if (options.CollectClientIpAddress || rateLimit.MissingPartitionKey != MissingPartitionKeyBehavior.Allow)
        {
            return;
        }

        // 容器此时已完全构建，解析结果与模块加载顺序无关。
        // 必须开作用域：提供者允许注册成 scoped，从 root 解析会抛。
        using var scope = context.ServiceProvider.CreateScope();
        if (scope.ServiceProvider.GetServices<IRateLimitPartitionKeyProvider>().Any())
        {
            return;
        }

        context.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger<AspNetCoreModule>()
            .LogWarning(
                "Rate limiting is enabled but cannot take effect for anonymous requests. "
                + "Client IP collection is off (AspNetCore:CollectClientIpAddress=false), no "
                + "IRateLimitPartitionKeyProvider is registered, and AspNetCore:RateLimit:MissingPartitionKey "
                + "is Allow - so every anonymous request has no partition key and is let through. "
                + "Register an IRateLimitPartitionKeyProvider, or set MissingPartitionKey to Deny or Global.");
    }

    /// <summary>
    /// 启动期自检：人机验证。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 配了 <c>AspNetCore:Captcha:Provider</c> 而没有同名实现 → <strong>启动即失败</strong>
    /// （<see cref="ICaptchaVerifier.EnsureConfigured"/> 抛 <see cref="ConfigurationException"/>）。
    /// 静默退回别的提供商会让配置、日志、接口全都正常而验证码换了一家。
    /// </para>
    /// <para>
    /// 没配提供商 → 放行是刻意的（消费方按需启用），但要把每一个挂了 <c>[RequireCaptcha]</c> 的端点点名记 Warning：
    /// 「挂了特性」与「有保护」在运行期长得一模一样，只有这条日志能把它们分开。
    /// 数据源是生效的路由表（<see cref="IActionDescriptorCollectionProvider"/>），被抑制的端点不会出现。
    /// </para>
    /// </remarks>
    private static void CheckCaptchaConfiguration(ApplicationInitializationContext context)
    {
        // 验证器与提供商允许注册成 scoped，从 root 解析会抛。
        using var scope = context.ServiceProvider.CreateScope();
        var verifier = scope.ServiceProvider.GetRequiredService<ICaptchaVerifier>();
        verifier.EnsureConfigured();

        if (verifier.IsEnabled)
        {
            return;
        }

        var gated = scope.ServiceProvider.GetRequiredService<IActionDescriptorCollectionProvider>()
            .ActionDescriptors.Items
            .Where(d => d.FilterDescriptors.Any(f => f.Filter is RequireCaptchaAttribute))
            .Select(d => d.AttributeRouteInfo?.Template ?? d.DisplayName ?? "(unknown)")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToList();

        if (gated.Count == 0)
        {
            return;
        }

        context.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger<AspNetCoreModule>()
            .LogWarning(
                "{Count} endpoint(s) carry [RequireCaptcha] but AspNetCore:Captcha:Provider is not configured, "
                + "so the gate lets every request through: {Endpoints}. Configure a provider "
                + "(recaptcha / recaptcha-v3 / hcaptcha / turnstile / altcha, or image when Tnzi.Identity is loaded) to enforce it.",
                gated.Count, string.Join(", ", gated));
    }

    /// <summary>
    /// 配置默认路由
    /// </summary>
    private static void ConfigureDefaultRoutes(WebApplication app)
    {
        // 获取配置
        var aspNetCoreOptions = app.Services.GetService<IOptions<AspNetCoreOptions>>()?.Value ?? new AspNetCoreOptions();
        var apiPathPrefix = aspNetCoreOptions.ApiPathPrefix ?? "/api";

        // 配置默认首页（欢迎页）。
        // 自带前端的宿主须关掉它：欢迎页是一个已匹配的端点，而静态文件中间件在
        // GetEndpoint() 非空时会让路，于是 wwwroot/index.html 恰好在 "/" 上、也只在 "/" 上取不到。
        // 见 AspNetCoreOptions.EnableWelcomePage。
        if (aspNetCoreOptions.EnableWelcomePage)
        {
            // 与请求无关的部分只算一次；路径相关的部分必须逐请求算，
            // 见 BuildWelcomePageRequestVars。
            var staticVars = BuildWelcomePageStaticVars(app.Services, apiPathPrefix);

            app.MapGet("/", (HttpContext httpContext) =>
            {
                var requestVars = BuildWelcomePageRequestVars(httpContext, apiPathPrefix);
                var html = GetWelcomePageHtml(staticVars, requestVars);
                return Microsoft.AspNetCore.Http.Results.Content(html, "text/html; charset=utf-8");
            });
        }

        // 配置 Area 路由支持
        app.MapControllerRoute(
            name: "areas",
            pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}");

        // 配置默认路由
        app.MapControllerRoute(
            name: "default",
            pattern: "{controller=Home}/{action=Index}/{id?}");

        // 映射所有控制器
        app.MapControllers();
    }

    private static IEnumerable<Assembly> GetApplicationPartAssemblies(Type moduleType)
    {
        var assemblies = new HashSet<Assembly> { moduleType.Assembly };
        var currentType = moduleType.BaseType;

        // 沿继承链向上扫描，直到不再是模块类型
        while (currentType != null &&
               typeof(ITnziModule).IsAssignableFrom(currentType) &&
               currentType != typeof(object))
        {
            // 跳过框架基类本身（TnziApplicationModule、TnziFrameworkModule 等）
            if (!currentType.IsAbstract || currentType.Assembly != typeof(ITnziModule).Assembly)
            {
                assemblies.Add(currentType.Assembly);
            }

            currentType = currentType.BaseType;
        }

        return assemblies;
    }

    /// <summary>
    /// 构建欢迎页面中与请求无关的模板变量（进程内只算一次）。
    /// </summary>
    private static Dictionary<string, string> BuildWelcomePageStaticVars(
        IServiceProvider services, string apiPathPrefix)
    {
        // 获取已加载模块数量
        var tnziApp = services.GetService<ITnziApplication>();
        var moduleCount = tnziApp?.Modules.Count.ToString() ?? "-";

        // 获取数据库提供者
        var dbOptions = services.GetService<IOptions<EFCore.Options.DatabaseOptions>>()?.Value;
        var dbProvider = dbOptions?.DbContexts.FirstOrDefault()?.Provider.ToString() ?? "-";

        // 获取框架版本
        var version = typeof(AspNetCoreModule).Assembly.GetName().Version;
        var versionStr = version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "0.1.0";

        return new Dictionary<string, string>
        {
            ["API_PATH_PREFIX"] = string.IsNullOrEmpty(apiPathPrefix) ? "(none)" : apiPathPrefix,
            ["MODULE_COUNT"] = moduleCount,
            ["DB_PROVIDER"] = dbProvider,
            ["VERSION"] = versionStr,
        };
    }

    /// <summary>
    /// 构建欢迎页面中随请求而变的模板变量 —— 页面上的每个链接，以及它自报的路由信息。
    ///
    /// 链接必须是带 PathBase 的<b>绝对路径</b>：欢迎页挂在 "/" 上，宿主一旦位于子路径下，
    /// 浏览器地址栏是 "https://host/api" 这样<b>没有尾斜杠</b>的形式，而相对地址
    /// "swagger/" 按 RFC 3986 会丢掉最后一段、解析成 "https://host/swagger/" —— 落在应用
    /// 之外，必然 404。
    ///
    /// PathBase 只能从 <paramref name="httpContext"/> 取，<b>不能</b>取配置里的
    /// AspNetCore:PathBase：子路径有三个来源（配置的 UsePathBase、ANCM 的 IIS 子应用、
    /// 反向代理的 X-Forwarded-Prefix），后两者在配置里是空的，而三者最终都汇聚到
    /// Request.PathBase。同理，页面自报的 PathBase / Effective API Path 也必须是这个运行时
    /// 真值：报配置值会让一个确实挂在 "/api" 之下的部署在页面上显示 "(none)"，把排障的人
    /// 直接引开。
    /// </summary>
    private static Dictionary<string, string> BuildWelcomePageRequestVars(
        HttpContext httpContext, string apiPathPrefix)
    {
        var pathBase = httpContext.Request.PathBase.Value?.TrimEnd('/') ?? string.Empty;

        // 计算外部客户端的有效 API 路径
        var effectiveApiPath = (pathBase + "/" + apiPathPrefix.TrimStart('/')).TrimEnd('/');
        if (string.IsNullOrEmpty(effectiveApiPath)) effectiveApiPath = "/";

        return new Dictionary<string, string>
        {
            ["PATH_BASE"] = string.IsNullOrEmpty(pathBase) ? "(none)" : pathBase,
            ["EFFECTIVE_API_PATH"] = effectiveApiPath,
            ["SWAGGER_UI_PATH"] = $"{pathBase}/swagger/",
            ["SWAGGER_JSON_PATH"] = $"{pathBase}/swagger/v1/swagger.json",
            ["HEALTH_PATH"] = $"{pathBase}/health",
        };
    }

    /// <summary>
    /// 生成欢迎页面 HTML（从嵌入资源加载，Tnzi.NET 官网风格）。
    ///
    /// 只有与请求无关的部分进缓存：<paramref name="requestVars"/> 里的 PathBase 可以随
    /// 请求变化（受信代理给的 X-Forwarded-Prefix、ANCM 子应用），逐请求替换而不进缓存键 ——
    /// 算进缓存键会让每个不同的前缀各占一份缓存、每次都重读嵌入资源。逐请求要做的只是几次字符串替换。
    /// </summary>
    private static string GetWelcomePageHtml(
        Dictionary<string, string> staticVars, Dictionary<string, string> requestVars)
    {
        var html = GetWelcomePageTemplate(staticVars);

        foreach (var (key, value) in requestVars)
        {
            html = html.Replace($"{{{{{key}}}}}", System.Net.WebUtility.HtmlEncode(value));
        }

        return html;
    }

    /// <summary>
    /// 读取嵌入的欢迎页模板并替换掉与请求无关的占位符，结果按其取值缓存。
    /// </summary>
    private static string GetWelcomePageTemplate(Dictionary<string, string> staticVars)
    {
        // 生成缓存键
        var cacheKey = string.Join("|", staticVars.Values);

        // 如果缓存有效，直接返回
        if (_cachedWelcomePageHtml != null && _cachedWelcomePageCacheKey == cacheKey)
        {
            return _cachedWelcomePageHtml;
        }

        lock (_welcomePageLock)
        {
            // 双重检查锁定
            if (_cachedWelcomePageHtml != null && _cachedWelcomePageCacheKey == cacheKey)
            {
                return _cachedWelcomePageHtml;
            }

            var assembly = typeof(AspNetCoreModule).Assembly;
            var resourceName = "Tnzi.AspNetCore.Resources.WelcomePage.html";

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                var fallback = "<html><body><h1>Welcome to Tnzi.NET</h1></body></html>";
                _cachedWelcomePageCacheKey = cacheKey;
                _cachedWelcomePageHtml = fallback;
                return fallback;
            }

            using var reader = new StreamReader(stream);
            var html = reader.ReadToEnd();

            // 替换所有模板占位符（HTML 编码防止 XSS）
            foreach (var (key, value) in staticVars)
            {
                html = html.Replace($"{{{{{key}}}}}", System.Net.WebUtility.HtmlEncode(value));
            }

            _cachedWelcomePageCacheKey = cacheKey;
            _cachedWelcomePageHtml = html;
            return html;
        }
    }
}
