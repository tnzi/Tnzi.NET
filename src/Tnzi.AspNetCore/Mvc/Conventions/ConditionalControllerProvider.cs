
namespace Tnzi.AspNetCore.Mvc.Conventions;

/// <summary>
/// 条件控制器提供者
/// 根据Controller的依赖是否可用来决定是否注册Controller
/// 用于支持Host模块的多个版本，根据依赖不同选择Controller的可见性
/// </summary>
public class ConditionalControllerProvider : IApplicationModelProvider
{
    private readonly IServiceCollection _services;

    /// <summary>
    /// 执行顺序，在ApiControllerRouteProvider之后执行
    /// 确保路由已设置后再检查依赖
    /// </summary>
    public int Order => -500;

    /// <summary>
    /// 初始化条件控制器提供者
    /// </summary>
    /// <param name="services">服务集合，用于检查依赖是否已注册</param>
    public ConditionalControllerProvider(IServiceCollection services)
    {
        _services = Check.NotNull(services);
    }

    /// <summary>
    /// 在其他提供者执行后调用
    /// </summary>
    public void OnProvidersExecuted(ApplicationModelProviderContext context)
    {
        // 不需要实现
    }

    /// <summary>
    /// 在其他提供者执行前调用
    /// 检查Controller的依赖是否可用，如果不可用则移除Controller
    /// </summary>
    public void OnProvidersExecuting(ApplicationModelProviderContext context)
    {
        var controllersToRemove = new List<ControllerModel>();

        foreach (var controller in context.Result.Controllers)
        {
            // 检查Controller的构造函数依赖是否可用
            if (!AreDependenciesAvailable(controller.ControllerType))
            {
                controllersToRemove.Add(controller);
            }
        }

        // 移除依赖不可用的Controller
        foreach (var controller in controllersToRemove)
        {
            context.Result.Controllers.Remove(controller);
        }
    }

    /// <summary>
    /// 检查Controller的所有构造函数依赖是否可用
    /// </summary>
    /// <param name="controllerType">Controller类型</param>
    /// <returns>如果所有依赖都可用则返回true，否则返回false</returns>
    private bool AreDependenciesAvailable(Type controllerType)
    {
        // 获取所有公共构造函数
        var constructors = controllerType.GetConstructors();

        // 如果没有公共构造函数，检查是否有默认构造函数
        if (constructors.Length == 0)
        {
            return true; // 无参构造函数，依赖可用
        }

        // 检查每个构造函数
        // 只要有一个构造函数的依赖都可用，就认为Controller可用
        foreach (var constructor in constructors)
        {
            var parameters = constructor.GetParameters();
            bool allDependenciesAvailable = true;

            foreach (var parameter in parameters)
            {
                var parameterType = parameter.ParameterType;

                // 跳过可选参数（有默认值的参数）
                if (parameter.HasDefaultValue)
                {
                    continue;
                }

                // 检查服务是否已注册
                if (!IsServiceRegistered(parameterType))
                {
                    allDependenciesAvailable = false;
                    break;
                }
            }

            // 如果这个构造函数的所有依赖都可用，则Controller可用
            if (allDependenciesAvailable)
            {
                return true;
            }
        }

        // 所有构造函数的依赖都不可用
        return false;
    }

    /// <summary>
    /// 检查服务类型是否已在服务集合中注册
    /// </summary>
    /// <param name="serviceType">服务类型</param>
    /// <returns>如果服务已注册则返回true，否则返回false</returns>
    private bool IsServiceRegistered(Type serviceType)
    {
        // 检查直接注册的服务（完全匹配）
        if (_services.Any(s => s.ServiceType == serviceType))
        {
            return true;
        }

        // 检查泛型服务（如 IRepository<TEntity, TKey>）
        if (serviceType.IsGenericType)
        {
            var genericTypeDefinition = serviceType.GetGenericTypeDefinition();
            if (_services.Any(s =>
                s.ServiceType.IsGenericType &&
                s.ServiceType.GetGenericTypeDefinition() == genericTypeDefinition))
            {
                return true;
            }
        }

        // 检查接口的实现
        // 在 ASP.NET Core DI 中，如果 ServiceType 是接口，ImplementationType 应该实现该接口
        // 如果 ServiceType 是类，它可能直接实现了该接口
        foreach (var service in _services)
        {
            // 检查 ServiceType 是否实现了目标接口（当 ServiceType 是类时）
            if (service.ServiceType != serviceType &&
                !service.ServiceType.IsInterface &&
                serviceType.IsAssignableFrom(service.ServiceType))
            {
                return true;
            }

            // 检查 ImplementationType 是否实现了目标接口
            if (service.ImplementationType != null &&
                serviceType.IsAssignableFrom(service.ImplementationType))
            {
                return true;
            }

            // 检查 ImplementationInstance 是否实现了目标接口
            if (service.ImplementationInstance != null &&
                serviceType.IsInstanceOfType(service.ImplementationInstance))
            {
                return true;
            }
        }

        return false;
    }
}