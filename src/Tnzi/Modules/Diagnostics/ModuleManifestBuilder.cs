namespace Tnzi.Modules.Diagnostics;

/// <summary>
/// 模块清单构建器 — 从 ServiceDescriptor 列表和程序集扫描中自动生成 <see cref="ModuleManifest"/>
/// </summary>
public static class ModuleManifestBuilder
{
    private static readonly Type EventHandlerOpenType = typeof(IEventHandler<>);
    private static readonly Type HostedServiceType = typeof(Microsoft.Extensions.Hosting.IHostedService);
    private static readonly Type ConfigureOptionsOpenType = typeof(IConfigureOptions<>);

    /// <summary>
    /// 根据模块描述符和已注册的服务构建模块清单
    /// </summary>
    /// <param name="module">模块描述符</param>
    /// <param name="serviceDescriptors">该模块注册的服务列表</param>
    /// <returns>自动生成的 <see cref="ModuleManifest"/></returns>
    public static ModuleManifest Build(IModuleDescriptor module, List<ServiceDescriptor> serviceDescriptors)
    {
        Check.NotNull(module);
        Check.NotNull(serviceDescriptors);

        var moduleAssembly = module.Assembly;
        var assemblyTypes = GetAssemblyTypes(moduleAssembly);

        return new ModuleManifest
        {
            Services = ExtractServices(serviceDescriptors, moduleAssembly),
            Controllers = ScanAssemblyTypes(assemblyTypes, IsControllerType),
            Events = ScanAssemblyTypes(assemblyTypes, IsEventHandler),
            BackgroundTasks = ExtractBackgroundTasks(serviceDescriptors),
            Options = ExtractOptions(serviceDescriptors, moduleAssembly)
        };
    }

    /// <summary>
    /// 从 ServiceDescriptor 列表中提取模块程序集内的服务注册
    /// </summary>
    private static IReadOnlyList<ServiceExport> ExtractServices(List<ServiceDescriptor> descriptors, System.Reflection.Assembly moduleAssembly)
    {
        var result = new List<ServiceExport>();

        foreach (var descriptor in descriptors)
        {
            var implType = descriptor.ImplementationType;
            // 跳过工厂委托注册（无具体实现类型）
            if (implType == null)
                continue;

            // 只包含来自模块程序集的实现类型
            if (implType.Assembly != moduleAssembly)
                continue;

            // 排除 IHostedService（单独在 BackgroundTasks 中展示）
            if (descriptor.ServiceType == HostedServiceType)
                continue;

            // 排除 IConfigureOptions<> 注册（单独在 Options 中展示）
            if (descriptor.ServiceType.IsGenericType &&
                descriptor.ServiceType.GetGenericTypeDefinition() == ConfigureOptionsOpenType)
                continue;

            result.Add(new ServiceExport(descriptor.ServiceType, implType, descriptor.Lifetime));
        }

        return result;
    }

    /// <summary>
    /// 从程序集安全获取所有类型（处理 ReflectionTypeLoadException）
    /// </summary>
    private static Type[] GetAssemblyTypes(System.Reflection.Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (System.Reflection.ReflectionTypeLoadException ex)
        {
            // 部分程序集加载失败时，返回已成功加载的类型
            return ex.Types.Where(t => t != null).ToArray()!;
        }
    }

    /// <summary>
    /// 扫描类型数组，返回满足谓词的具体类（排除抽象类）的简单名称列表
    /// </summary>
    private static IReadOnlyList<string> ScanAssemblyTypes(Type[] types, Func<Type, bool> predicate)
    {
        var result = new List<string>();

        foreach (var type in types)
        {
            if (!type.IsClass || type.IsAbstract)
                continue;

            if (predicate(type))
                result.Add(type.Name);
        }

        return result;
    }

    /// <summary>
    /// 通过基类名称链检测是否为控制器类型（不依赖 ASP.NET Core 程序集引用）
    /// </summary>
    private static bool IsControllerType(Type type)
    {
        var current = type.BaseType;
        while (current != null && current != typeof(object))
        {
            var name = current.Name;
            if (name is "ControllerBase" or "Controller" or "ApiControllerBase" or "ApiAdminControllerBase")
                return true;
            current = current.BaseType;
        }
        return false;
    }

    /// <summary>
    /// 检查类型是否实现了 IEventHandler&lt;TEvent&gt;
    /// </summary>
    private static bool IsEventHandler(Type type)
    {
        return type.GetInterfaces().Any(i =>
            i.IsGenericType && i.GetGenericTypeDefinition() == EventHandlerOpenType);
    }

    /// <summary>
    /// 从 ServiceDescriptor 列表中提取 IHostedService 注册（后台任务）
    /// </summary>
    private static IReadOnlyList<string> ExtractBackgroundTasks(List<ServiceDescriptor> descriptors)
    {
        var result = new List<string>();

        foreach (var descriptor in descriptors)
        {
            if (descriptor.ServiceType != HostedServiceType)
                continue;

            var implType = descriptor.ImplementationType;
            if (implType != null)
                result.Add(implType.Name);
        }

        return result;
    }

    /// <summary>
    /// 从 ServiceDescriptor 列表中提取模块程序集内的 IConfigureOptions&lt;T&gt; 注册
    /// </summary>
    private static IReadOnlyList<string> ExtractOptions(List<ServiceDescriptor> descriptors, System.Reflection.Assembly moduleAssembly)
    {
        var result = new List<string>();
        var seen = new HashSet<Type>();

        foreach (var descriptor in descriptors)
        {
            var serviceType = descriptor.ServiceType;
            if (!serviceType.IsGenericType)
                continue;

            if (serviceType.GetGenericTypeDefinition() != ConfigureOptionsOpenType)
                continue;

            // TOptions 类型参数
            var optionsType = serviceType.GetGenericArguments()[0];

            // 只包含来自模块程序集的 Options 类型
            if (optionsType.Assembly != moduleAssembly)
                continue;

            if (seen.Add(optionsType))
                result.Add(optionsType.Name);
        }

        return result;
    }
}
