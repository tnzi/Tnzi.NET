
namespace Tnzi.AspNetCore.Mvc.Conventions;

/// <summary>
/// 条件控制器提供者
/// 根据Controller的依赖是否可用来决定是否注册Controller
/// 用于支持Host模块的多个版本，根据依赖不同选择Controller的可见性
/// </summary>
public class ConditionalControllerProvider : IApplicationModelProvider
{
    /// <summary>
    /// 已注册的服务类型集合（在构造时快照，避免长期持有整个 IServiceCollection）
    /// </summary>
    private readonly HashSet<Type> _registeredServiceTypes;
    private readonly HashSet<Type> _registeredGenericDefinitions;
    private readonly List<(Type ServiceType, Type? ImplementationType, object? ImplementationInstance)> _serviceEntries;
    private readonly ControllerActivationDiagnostics? _diagnostics;

    /// <summary>
    /// 执行顺序，在ApiControllerRouteProvider之后执行
    /// 确保路由已设置后再检查依赖
    /// </summary>
    public int Order => -500;

    /// <summary>
    /// 初始化条件控制器提供者
    /// </summary>
    /// <param name="services">服务集合，用于检查依赖是否已注册</param>
    /// <param name="diagnostics">可选的诊断收集器</param>
    public ConditionalControllerProvider(IServiceCollection services, ControllerActivationDiagnostics? diagnostics = null)
    {
        Check.NotNull(services);

        // 快照服务类型，避免长期持有整个 IServiceCollection
        _registeredServiceTypes = new HashSet<Type>(services.Select(s => s.ServiceType));
        _registeredGenericDefinitions = new HashSet<Type>(
            services.Where(s => s.ServiceType.IsGenericType)
                    .Select(s => s.ServiceType.GetGenericTypeDefinition()));
        _serviceEntries = services.Select(s => (s.ServiceType, s.ImplementationType, s.ImplementationInstance)).ToList();
        _diagnostics = diagnostics;
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
        var controllersToRemove = new List<(ControllerModel Controller, Type? MissingDependency)>();

        foreach (var controller in context.Result.Controllers)
        {
            // 检查Controller的构造函数依赖是否可用
            if (!AreDependenciesAvailable(controller.ControllerType, out var missingDependency))
            {
                controllersToRemove.Add((controller, missingDependency));
            }
        }

        // 移除依赖不可用的Controller
        foreach (var (controller, missingDependency) in controllersToRemove)
        {
            context.Result.Controllers.Remove(controller);
            _diagnostics?.RecordSuppression(
                controller.ControllerType.FullName ?? controller.ControllerType.Name,
                $"Missing dependency: {missingDependency?.FullName ?? missingDependency?.Name ?? "unknown"}",
                SuppressionReason.MissingDependency);
        }
    }

    /// <summary>
    /// 检查Controller的所有构造函数依赖是否可用
    /// </summary>
    /// <param name="controllerType">Controller类型</param>
    /// <param name="missingDependency">第一个缺失的依赖类型（如果有）</param>
    /// <returns>如果所有依赖都可用则返回true，否则返回false</returns>
    private bool AreDependenciesAvailable(Type controllerType, out Type? missingDependency)
    {
        missingDependency = null;

        // 获取所有公共构造函数
        var constructors = controllerType.GetConstructors();

        // 如果没有公共构造函数，检查是否有默认构造函数
        if (constructors.Length == 0)
        {
            return true; // 无参构造函数，依赖可用
        }

        // 记录最后一个构造函数中缺失的依赖类型
        Type? lastMissing = null;

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
                    lastMissing = parameterType;
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
        missingDependency = lastMissing;
        return false;
    }

    /// <summary>
    /// 检查服务类型是否已在服务集合中注册
    /// </summary>
    /// <param name="serviceType">服务类型</param>
    /// <returns>如果服务已注册则返回true，否则返回false</returns>
    private bool IsServiceRegistered(Type serviceType)
    {
        // 检查直接注册的服务（完全匹配，O(1) HashSet 查找）
        if (_registeredServiceTypes.Contains(serviceType))
        {
            return true;
        }

        // 检查泛型服务（如 IRepository<TEntity, TKey>）
        if (serviceType.IsGenericType)
        {
            var genericTypeDefinition = serviceType.GetGenericTypeDefinition();
            if (_registeredGenericDefinitions.Contains(genericTypeDefinition))
            {
                return true;
            }
        }

        // 检查接口的实现（使用快照数据）
        foreach (var (svcType, implType, implInstance) in _serviceEntries)
        {
            // 检查 ServiceType 是否实现了目标接口（当 ServiceType 是类时）
            if (svcType != serviceType &&
                !svcType.IsInterface &&
                serviceType.IsAssignableFrom(svcType))
            {
                return true;
            }

            // 检查 ImplementationType 是否实现了目标接口
            if (implType != null && serviceType.IsAssignableFrom(implType))
            {
                return true;
            }

            // 检查 ImplementationInstance 是否实现了目标接口
            if (implInstance != null && serviceType.IsInstanceOfType(implInstance))
            {
                return true;
            }
        }

        return false;
    }
}