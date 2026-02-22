
namespace Tnzi.AspNetCore.Extensions;

/// <summary>
/// ServiceConfigurationContext 扩展方法
/// </summary>
public static class ServiceConfigurationContextExtensions
{
    // 缓存空的服务提供者，避免重复创建
    private static readonly IServiceProvider EmptyServiceProvider = new ServiceCollection().BuildServiceProvider();

    /// <summary>
    /// 注册控制器程序集
    /// 自动从服务容器中获取 IMvcBuilder 并注册指定模块的程序集
    /// </summary>
    /// <typeparam name="TModule">模块类型（用于获取程序集）</typeparam>
    /// <param name="context">服务配置上下文</param>
    /// <returns>服务配置上下文（支持链式调用）</returns>
    public static ServiceConfigurationContext AddControllerAssembly<TModule>(this ServiceConfigurationContext context)
        where TModule : class
    {
        Check.NotNull(context);

        // 从服务容器中查找 IMvcBuilder
        var mvcBuilderDescriptor = context.Services.FirstOrDefault(s => s.ServiceType == typeof(IMvcBuilder));
        if (mvcBuilderDescriptor == null)
        {
            // IMvcBuilder 不存在，静默跳过（适用于非Web应用场景）
            return context;
        }

        IMvcBuilder? mvcBuilder = null;

        // 尝试从 ImplementationInstance 获取（直接存储实例的情况）
        if (mvcBuilderDescriptor.ImplementationInstance is IMvcBuilder instance)
        {
            mvcBuilder = instance;
        }
        // 尝试从 ImplementationFactory 获取（通过工厂方法存储的情况）
        else if (mvcBuilderDescriptor.ImplementationFactory != null)
        {
            // 工厂方法通常不需要依赖（如 AspNetCoreModule 中的 _ => mvcBuilder）
            // 使用缓存的空服务提供者即可
            mvcBuilder = mvcBuilderDescriptor.ImplementationFactory(EmptyServiceProvider) as IMvcBuilder;
        }

        // 如果成功获取 IMvcBuilder，注册程序集
        if (mvcBuilder != null)
        {
            mvcBuilder.AddApplicationPart(typeof(TModule).Assembly);
        }

        return context;
    }
}