
namespace Tnzi.AspNetCore.Extensions;

/// <summary>
/// ServiceConfigurationContext 扩展方法
/// </summary>
public static class ServiceConfigurationContextExtensions
{
    /// <summary>
    /// 注册控制器程序集
    /// 自动从 ITnziApplication 获取 MvcBuilder 并注册指定模块的程序集
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

        // 尝试从 ImplementationInstance 获取（直接存储实例的情况）
        if (mvcBuilderDescriptor.ImplementationInstance is IMvcBuilder mvcBuilder)
        {
            mvcBuilder.AddApplicationPart(typeof(TModule).Assembly);
        }

        return context;
    }
}
