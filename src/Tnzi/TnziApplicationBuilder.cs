namespace Tnzi;

/// <summary>
/// Tnzi 应用程序构建器
/// 提供 Fluent API 风格的应用配置
/// </summary>
public class TnziApplicationBuilder
{
    private readonly IServiceCollection _services;
    private readonly IConfiguration _configuration;
    private Type? _startupModuleType;
    private Action<TnziOptions>? _optionsAction;

    /// <summary>
    /// 创建 Tnzi 应用程序构建器
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configuration">配置对象</param>
    public TnziApplicationBuilder(IServiceCollection services, IConfiguration configuration)
    {
        _services = Check.NotNull(services);
        _configuration = Check.NotNull(configuration);
    }

    /// <summary>
    /// 指定启动模块
    /// </summary>
    /// <typeparam name="TModule">启动模块类型</typeparam>
    /// <returns>构建器实例（支持链式调用）</returns>
    public TnziApplicationBuilder UseStartupModule<TModule>() where TModule : ITnziModule
    {
        _startupModuleType = typeof(TModule);
        return this;
    }

    /// <summary>
    /// 指定启动模块（非泛型重载）
    /// </summary>
    /// <param name="moduleType">启动模块类型</param>
    /// <returns>构建器实例（支持链式调用）</returns>
    public TnziApplicationBuilder UseStartupModule(Type moduleType)
    {
        if (!typeof(ITnziModule).IsAssignableFrom(moduleType))
        {
            throw new ArgumentException($"Type {moduleType.FullName} does not implement ITnziModule", nameof(moduleType));
        }
        _startupModuleType = moduleType;
        return this;
    }

    /// <summary>
    /// 配置 Tnzi 选项
    /// </summary>
    /// <param name="configure">配置委托</param>
    /// <returns>构建器实例（支持链式调用）</returns>
    public TnziApplicationBuilder ConfigureOptions(Action<TnziOptions> configure)
    {
        _optionsAction = configure;
        return this;
    }

    /// <summary>
    /// 构建 Tnzi 应用程序
    /// </summary>
    /// <returns>Tnzi 应用程序实例</returns>
    public TnziApplication Build()
    {
        if (_startupModuleType == null)
        {
            throw new InvalidOperationException("Startup module not specified. Call UseStartupModule<T>() first.");
        }

        // 注册选项
        if (_optionsAction != null)
        {
            _services.Configure(_optionsAction);
        }
        else
        {
            _services.Configure<TnziOptions>(_ => { });
        }

        return new TnziApplication(_startupModuleType, _services, _configuration);
    }
}

/// <summary>
/// IServiceCollection 扩展方法
/// </summary>
public static class TnziApplicationBuilderExtensions
{
    /// <summary>
    /// 创建 Tnzi 应用程序构建器
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configuration">配置对象</param>
    /// <returns>Tnzi 应用程序构建器</returns>
    public static TnziApplicationBuilder AddTnzi(this IServiceCollection services, IConfiguration configuration)
    {
        return new TnziApplicationBuilder(services, configuration);
    }
}
