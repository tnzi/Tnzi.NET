
namespace Tnzi.Modules;

/// <summary>
/// 模块类型（决定默认加载优先级）
/// </summary>
public enum ModuleType
{
    /// <summary>
    /// 核心模块（最高优先级，加载顺序：0-99）
    /// 如：日志、配置等基础服务
    /// </summary>
    Core = 0,
    
    /// <summary>
    /// 基础设施模块（加载顺序：100-199）
    /// 如：缓存、事件总线、ORM等
    /// </summary>
    Infrastructure = 100,
    
    /// <summary>
    /// 框架模块（加载顺序：200-299）
    /// 如：ASP.NET Core、SignalR等
    /// </summary>
    Framework = 200,
    
    /// <summary>
    /// 业务模块（加载顺序：300-399）
    /// 如：Identity、Authorization等
    /// </summary>
    Application = 300,
    
    /// <summary>
    /// 自定义/扩展模块（最低优先级，加载顺序：400+）
    /// </summary>
    Custom = 400
}

/// <summary>
/// Tnzi 模块接口
/// 所有生命周期方法均为异步，支持异步初始化场景
/// </summary>
[StableApi(Since = "0.1.0")]
public interface ITnziModule
{
    /// <summary>
    /// 模块类型（决定默认加载优先级）
    /// </summary>
    ModuleType ModuleType { get; }
    
    /// <summary>
    /// 模块加载顺序（在同类型模块中的顺序，默认0）
    /// 数值越小越先加载
    /// </summary>
    int LoadOrder { get; }
    
    /// <summary>
    /// 预配置服务（在 ConfigureServices 之前执行）
    /// 用于注册配置选项、验证器等
    /// </summary>
    Task PreConfigureServicesAsync(ServiceConfigurationContext context);
    
    /// <summary>
    /// 配置服务
    /// </summary>
    Task ConfigureServicesAsync(ServiceConfigurationContext context);
    
    /// <summary>
    /// 后配置服务（在所有模块 ConfigureServices 之后执行）
    /// 用于覆盖或补充其他模块的配置
    /// </summary>
    Task PostConfigureServicesAsync(ServiceConfigurationContext context);
    
    /// <summary>
    /// 配置中间件/应用构建
    /// </summary>
    Task OnApplicationInitializationAsync(ApplicationInitializationContext context);
    
    /// <summary>
    /// 应用关闭时执行
    /// 用于清理资源、保存状态等
    /// </summary>
    Task OnApplicationShutdownAsync(ApplicationShutdownContext context);
}

/// <summary>
/// 服务配置上下文
/// </summary>
public class ServiceConfigurationContext
{
    /// <summary>
    /// 服务集合
    /// </summary>
    public IServiceCollection Services { get; }
    
    /// <summary>
    /// 配置对象
    /// </summary>
    public IConfiguration Configuration { get; }
    
    public ServiceConfigurationContext(IServiceCollection services, IConfiguration configuration)
    {
        Services = Check.NotNull(services);
        Configuration = Check.NotNull(configuration);
    }
}

/// <summary>
/// 应用初始化上下文
/// </summary>
public class ApplicationInitializationContext
{
    /// <summary>
    /// 服务提供者
    /// </summary>
    public IServiceProvider ServiceProvider { get; }
    
    /// <summary>
    /// ASP.NET Core 应用构建器
    /// </summary>
    public IApplicationBuilder? App { get; }
    
    /// <summary>
    /// 托管环境
    /// </summary>
    public IWebHostEnvironment? Environment { get; }
    
    /// <summary>
    /// 获取或设置 WebApplication 实例（如果可用）
    /// </summary>
    public WebApplication? WebApp { get; }
    
    public ApplicationInitializationContext(
        IServiceProvider serviceProvider,
        IApplicationBuilder? app = null,
        IWebHostEnvironment? environment = null,
        WebApplication? webApp = null)
    {
        ServiceProvider = Check.NotNull(serviceProvider);
        App = app;
        Environment = environment;
        WebApp = webApp;
    }
}

/// <summary>
/// 应用关闭上下文
/// </summary>
public class ApplicationShutdownContext
{
    /// <summary>
    /// 服务提供者
    /// </summary>
    public IServiceProvider ServiceProvider { get; }
    
    public ApplicationShutdownContext(IServiceProvider serviceProvider)
    {
        ServiceProvider = Check.NotNull(serviceProvider);
    }
}