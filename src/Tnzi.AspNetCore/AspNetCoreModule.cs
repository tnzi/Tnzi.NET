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
    private static string? _cachedApiPathPrefix;
    private static readonly object _welcomePageLock = new();

    /// <summary>
    /// 框架模块默认加载顺序
    /// </summary>
    public override int LoadOrder => 0;

    public override Task PreConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // 注册配置选项并启用启动时验证
        context.Services.AddOptions<AspNetCoreOptions>()
            .Bind(context.Configuration.GetSection("AspNetCore"))
            .ValidateWith<AspNetCoreOptions, AspNetCoreOptionsValidator>();

        return Task.CompletedTask;
    }

    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // 自动添加 Controllers 支持
        var mvcBuilder = context.Services.AddControllers();

        // 自动注册所有业务模块的程序集到 ApplicationPartManager
        // 替代 TnziApplication.RegisterControllerAssemblies 的非标准做法
        var appDescriptor = context.Services.FirstOrDefault(s => s.ServiceType == typeof(ITnziApplication));
        if (appDescriptor?.ImplementationInstance is ITnziApplication app)
        {
            foreach (var module in app.Modules)
            {
                // 仅注册业务模块 (TnziApplicationModule) 的程序集
                if (module.Instance is TnziApplicationModule)
                {
                    mvcBuilder.AddApplicationPart(module.Type.Assembly);
                }
            }
        }

        context.Services.AddHttpContextAccessor();

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
        // 替换 EFCoreModule 注册的 DesignTimeCurrentUser 为 HttpContextCurrentUser
        // 使用 RemoveAll 确保移除所有已有的 ICurrentUser 注册
        context.Services.RemoveAll<ICurrentUser>();
        context.Services.AddTransient<ICurrentUser, HttpContextCurrentUser>();

        // 注册作用域上下文
        context.Services.AddScoped<IScopedContext, ScopedContext.HttpContextScopedContext>();

        // 注册HTTP加密服务
        context.Services.TryAddTransient<IClientHttpCrypto, ClientHttpCrypto>();
        context.Services.TryAddTransient<IHostHttpCrypto, HostHttpCrypto>();

        // 读取并配置 AspNetCore 选项
        var aspNetCoreOptions = context.Configuration
            .GetSection("AspNetCore")
            .Get<AspNetCoreOptions>() ?? new AspNetCoreOptions();

        // 注册过滤器（根据配置）
        context.Services.Configure<MvcOptions>(options =>
        {
            // 注册StringTrimModelBinder
            options.ModelBinderProviders.Insert(0, new StringTrimModelBinderProvider());

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
                _ => new Mvc.Conventions.ApiControllerRouteProvider(aspNetCoreOptions));
        }

        // 注册条件控制器提供者，用于根据依赖可用性过滤Controller
        // 支持Host模块的多个版本，根据依赖不同选择Controller的可见性
        context.Services.AddSingleton<IApplicationModelProvider>(
            _ => new Mvc.Conventions.ConditionalControllerProvider(context.Services));

        // 注册过滤器服务
        // API 结果包装过滤器
        context.Services.AddScoped<ApiResultWrapperFilter>();

        // 注册过滤器服务（如果工作单元过滤器启用）
        // 注意：UnitOfWorkFilter 现在支持可选标记模式，即使全局模式未启用也会注册
        // 但只有在全局模式或标记了 [UnitOfWork] 时才会实际启用事务
        context.Services.AddScoped<UnitOfWorkFilter>();

        // 注册 UnitOfWorkActionFilter（用于可选标记模式）
        context.Services.AddScoped<UnitOfWorkActionFilter>();

        // 注册请求验证服务（如果启用）
        if (aspNetCoreOptions.RequestValidation?.Enabled == true)
        {
            context.Services.AddScoped<IRequestValidator, Security.RequestValidator>();
        }
        // 注册限流服务（如果启用）
        if (aspNetCoreOptions.RateLimit?.Enabled == true)
        {
            context.Services.TryAddScoped<IRateLimitService, Security.RateLimitService>();
        }

        // 注册异常统计服务（如果启用）
        if (aspNetCoreOptions.ExceptionHandling?.EnableMetrics == true)
        {
            var exceptionHandling = aspNetCoreOptions.ExceptionHandling;
            context.Services.AddSingleton<IExceptionStatistics>(sp =>
            {
                var historySize = exceptionHandling!.ExceptionHistorySize;
                return new ExceptionStatistics(historySize);
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

            // ===================================================================
            // 中间件注册顺序说明（从外到内）：
            // 0. ForwardedHeaders - 最外层，确保所有后续中间件获取正确的客户端 IP 和协议
            // 1. ExceptionHandling - 捕获所有下游中间件的异常
            // 2. RequestTracking  - 请求追踪，生成 RequestId，确保异常响应也带 RequestId
            // 3. CORS             - 跨域处理，确保错误响应也包含 CORS 头
            // 4. Localization     - 本地化，确保异常消息使用正确的 Culture
            // 5. HostHttpCrypto   - HTTP 加密/解密，异常可被外层捕获
            // 6. SecurityHeaders  - 安全响应头
            // 7. RateLimiting     - 限流，在请求验证之前拦截恶意流量
            // 8. RequestValidation- 请求验证，已通过限流检查的请求才做验证
            // 9. ApiVersion       - API 版本控制
            // 10. ResponseCompression - 响应压缩
            // 11. Authentication + Authorization - 认证和授权
            // 12. SPANotFound     - SPA 404 处理（最内层）
            // ===================================================================

            // 0. ForwardedHeaders 中间件（最外层，处理代理服务器转发的协议、主机和IP）
            // 必须在异常处理之前，确保所有后续中间件都能获取正确的客户端 IP 和协议
            if (aspNetCoreOptions.EnableForwardedHeaders)
            {
                app.UseForwardedHeaders(new ForwardedHeadersOptions
                {
                    ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor |
                                     Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto |
                                     Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedHost
                });

                // 处理 X-Forwarded-Prefix 并设置为 PathBase
                app.Use((context, next) =>
                {
                    if (context.Request.Headers.TryGetValue("X-Forwarded-Prefix", out var prefix) && !string.IsNullOrWhiteSpace(prefix))
                    {
                        var pathBase = prefix.ToString();
                        if (!pathBase.StartsWith('/'))
                        {
                            pathBase = "/" + pathBase;
                        }
                        context.Request.PathBase = new PathString(pathBase);
                    }
                    return next();
                });
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

            // 6. 安全头部中间件（如果启用）
            if (aspNetCoreOptions.SecurityHeaders?.EnableSecurityHeaders == true)
            {
                app.UseMiddleware<SecurityHeadersMiddleware>();
            }

            // 7. 限流中间件（在请求验证之前，先拦截恶意高频流量，避免无效请求消耗验证资源）
            if (aspNetCoreOptions.RateLimit?.Enabled == true)
            {
                app.UseMiddleware<RateLimitingMiddleware>();
            }

            // 8. 请求验证中间件（在限流之后，只对通过限流检查的请求进行验证）
            if (aspNetCoreOptions.RequestValidation?.Enabled == true)
            {
                app.UseMiddleware<RequestValidationMiddleware>();
            }

            // 9. API 版本控制中间件（如果启用）
            if (aspNetCoreOptions.ApiVersion?.Enabled == true)
            {
                app.UseMiddleware<Versioning.ApiVersionMiddleware>();
            }

            // 10. 响应压缩中间件（如果启用）
            if (aspNetCoreOptions.EnableResponseCompression)
            {
                app.UseResponseCompression();
            }

            // 11. 认证和授权中间件（必须在路由之前调用）
            app.UseAuthentication();
            app.UseAuthorization();

            // 12. SPA 404 处理中间件（最内层，可选）
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

        return Task.CompletedTask;
    }

    /// <summary>
    /// 配置默认路由
    /// </summary>
    private static void ConfigureDefaultRoutes(WebApplication app)
    {
        // 获取 API 路径前缀配置
        var aspNetCoreOptions = app.Services.GetService<IOptions<AspNetCoreOptions>>()?.Value ?? new AspNetCoreOptions();
        var apiPathPrefix = aspNetCoreOptions.ApiPathPrefix ?? "/api";

        // 配置默认首页（美观的 HTML 页面）
        app.MapGet("/", () =>
        {
            var html = GetWelcomePageHtml(apiPathPrefix);
            return Microsoft.AspNetCore.Http.Results.Content(html, "text/html; charset=utf-8");
        });

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

    /// <summary>
    /// 生成欢迎页面 HTML（从嵌入资源加载，Adobe 暗黑系风格）
    /// 使用缓存避免每次请求都读取嵌入资源
    /// </summary>
    private static string GetWelcomePageHtml(string apiPathPrefix)
    {
        // 如果缓存有效（参数未变），直接返回缓存的 HTML
        if (_cachedWelcomePageHtml != null && _cachedApiPathPrefix == apiPathPrefix)
        {
            return _cachedWelcomePageHtml;
        }

        lock (_welcomePageLock)
        {
            // 双重检查锁定
            if (_cachedWelcomePageHtml != null && _cachedApiPathPrefix == apiPathPrefix)
            {
                return _cachedWelcomePageHtml;
            }

            // 对 apiPathPrefix 进行 HTML 编码，防止 XSS
            var encodedPrefix = System.Net.WebUtility.HtmlEncode(apiPathPrefix);

            var assembly = typeof(AspNetCoreModule).Assembly;
            var resourceName = "Tnzi.AspNetCore.Resources.WelcomePage.html";

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                // 如果资源不存在，返回简单的欢迎页面
                var fallback = $"<html><body><h1>Welcome to Tnzi.NET</h1><p>API Path: {encodedPrefix}</p></body></html>";
                _cachedApiPathPrefix = apiPathPrefix;
                _cachedWelcomePageHtml = fallback;
                return fallback;
            }

            using var reader = new StreamReader(stream);
            var html = reader.ReadToEnd();

            // 替换占位符并缓存
            var result = html.Replace("{{API_PATH_PREFIX}}", encodedPrefix);
            _cachedApiPathPrefix = apiPathPrefix;
            _cachedWelcomePageHtml = result;
            return result;
        }
    }
}
