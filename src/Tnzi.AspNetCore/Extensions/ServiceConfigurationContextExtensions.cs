namespace Tnzi.AspNetCore.Extensions;

/// <summary>
/// ServiceConfigurationContext 扩展方法
/// </summary>
public static class ServiceConfigurationContextExtensions
{
    /// <summary>
    /// 把 <typeparamref name="TModule"/> 所在程序集登记为 MVC 应用部件，使其中的控制器进入路由表。
    /// </summary>
    /// <remarks>
    /// 已加载模块的程序集由 <c>AspNetCoreModule</c> 自动登记，这个方法用于不是模块的类库（例如只放控制器的插件程序集）。
    /// 须在 <c>AspNetCoreModule</c> 的 <c>ConfigureServicesAsync</c> 之后调用（依赖它的模块在自己的 <c>ConfigureServicesAsync</c> 里调用即可）；
    /// MVC 尚未注册时抛异常而不是静默跳过：静默跳过的后果是控制器全部 404 且没有任何日志。
    /// </remarks>
    /// <typeparam name="TModule">程序集里的任一类型（用于定位程序集）</typeparam>
    /// <param name="context">服务配置上下文</param>
    /// <returns>服务配置上下文（支持链式调用）</returns>
    /// <exception cref="InvalidOperationException">MVC 尚未注册（服务集合里没有 <see cref="ApplicationPartManager"/>）。</exception>
    public static ServiceConfigurationContext AddControllerAssembly<TModule>(this ServiceConfigurationContext context)
        where TModule : class
    {
        Check.NotNull(context);

        // AddControllers / AddMvcCore 把 ApplicationPartManager 以实例形式注册进服务集合；IMvcBuilder 从来不是服务
        var partManager = context.Services
            .LastOrDefault(s => s.ServiceType == typeof(ApplicationPartManager))?
            .ImplementationInstance as ApplicationPartManager
            ?? throw new InvalidOperationException(
                $"Cannot register controllers from {typeof(TModule).Assembly.GetName().Name}: MVC is not registered yet. " +
                "Call AddControllerAssembly from a module that depends on AspNetCoreModule, in ConfigureServicesAsync or later.");

        var assembly = typeof(TModule).Assembly;
        if (!partManager.ApplicationParts.OfType<AssemblyPart>().Any(part => part.Assembly == assembly))
        {
            partManager.ApplicationParts.Add(new AssemblyPart(assembly));
        }

        return context;
    }
}
