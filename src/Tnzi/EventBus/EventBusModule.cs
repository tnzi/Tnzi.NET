
namespace Tnzi.EventBus;

/// <summary>
/// 事件总线模块
/// 配置路径：EventBus
/// </summary>
[DependsOn(typeof(CachingModule))]
public class EventBusModule : TnziInfrastructureModule
{
    /// <summary>
    /// 本模块在 <see cref="ConfigureServicesAsync"/> 里自动注册出的处理器描述符，
    /// 留到 <see cref="PostConfigureServicesAsync"/> 与手动注册对账。
    /// </summary>
    private List<ServiceDescriptor> _autoRegisteredHandlers = [];

    public override int LoadOrder => 10;

    public override Task PreConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // 配置EventBusOptions并启用启动时验证
        context.Services.AddTnziOptions<EventBusOptions, EventBusOptionsValidator>(context.Configuration);
        return Task.CompletedTask;
    }

    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        var services = context.Services;
        var configuration = context.Configuration;

        // 从配置中读取 EventBusOptions（手动绑定，避免构建 ServiceProvider）
        // 注意：PreConfigureServicesAsync 中已绑定配置用于验证，这里重新读取是为了在无法构建
        // ServiceProvider 的情况下获取配置值。这是必要的，因为自动注册需要在 ConfigureServicesAsync
        // 阶段完成，而此时服务容器尚未构建完成。
        var eventBusSection = configuration.GetSection("EventBus");
        var options = new EventBusOptions();
        eventBusSection.Bind(options);

        // 确保默认值被正确应用（Bind 不会覆盖未配置的属性）
        // EventBusOptions 的默认值已在属性定义中设置，Bind 只会覆盖配置文件中存在的值

        // 如果启用自动注册，执行自动注册逻辑
        // 注意：此时无法获取 ILogger，使用 NullLogger（日志将在运行时通过 IEventBus 记录）
        _autoRegisteredHandlers = [];
        if (options.AutoRegisterHandlers)
        {
            // 使用 NullLogger，因为此时服务还未完全注册
            // 实际的注册信息会在运行时通过 LocalEventBus 的日志记录
            var logger = NullLogger<EventBusModule>.Instance;
            AutoRegisterEventHandlers(services, options, logger);
        }

        // 注册死信队列（如果启用）
        if (options.EnableDeadLetterQueue)
        {
            services.AddSingleton<IEventDeadLetterQueue, InMemoryEventDeadLetterQueue>();
        }

        // 注册事件总线
        // 注意：ICurrentTenant 是 Scoped 服务，不能在 Singleton 工厂中解析并持有引用
        // LocalEventBus 在 PublishAsync 中通过 scope 动态解析 ICurrentTenant
        //
        // 注册语义(重要)：
        // - LocalEventBus 同时注册为 ILocalEventBus 与 IEventBus,IEventBus 永远指向本地总线;
        // - 分布式实现(RabbitMQ/Kafka)只注册 IDistributedEventBus/IIntegrationEventBus,
        //   不再替换 IEventBus —— 进程内领域事件在任何配置下都由本地总线派发
        services.AddSingleton<LocalEventBus>(provider =>
        {
            var eventBusLogger = provider.GetRequiredService<ILogger<LocalEventBus>>();
            var eventBusOptions = provider.GetService<IOptions<EventBusOptions>>()?.Value ?? new EventBusOptions();
            var maxConcurrency = eventBusOptions.MaxConcurrency;
            var deadLetterQueue = options.EnableDeadLetterQueue
                ? provider.GetService<IEventDeadLetterQueue>()
                : null;

            return new LocalEventBus(provider, eventBusLogger, eventBusOptions, deadLetterQueue, maxConcurrency);
        });
        services.AddSingleton<ILocalEventBus>(provider => provider.GetRequiredService<LocalEventBus>());
        services.AddSingleton<IEventBus>(provider => provider.GetRequiredService<LocalEventBus>());

        return Task.CompletedTask;
    }

    /// <summary>
    /// 撤下被手动注册覆盖的自动注册：同一个处理器每次事件只执行一次，生命周期以手动注册为准。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 本模块（LoadOrder 10）先于应用模块执行 <see cref="ConfigureServicesAsync"/>，扫描那一刻看不见应用模块随后写的
    /// <c>AddEventHandler</c>，<see cref="IsAlreadyRegistered"/> 只拦得住更早的注册。两条描述符并存时同一个处理器
    /// 每次事件执行两遍（重复发信、重复扣减），没有任何告警；而文档与示例都在推荐应用层手动注册。
    /// 到 PostConfigure 时所有模块的 <c>ConfigureServicesAsync</c> 都已执行完，对账在这里做。
    /// </para>
    /// <para>
    /// 刻意不把自动注册整个挪到这里：应用模块在 <c>ConfigureServicesAsync</c> 里 <c>RemoveEventHandler</c> /
    /// <c>ReplaceEventHandler</c> 掉的自动注册处理器会被重新加回来。
    /// 认不出的两种情形：经工厂委托注册的处理器（描述符上看不到实现类型），以及排在本模块之后的 PostConfigure 里才写的注册。
    /// </para>
    /// </remarks>
    public override Task PostConfigureServicesAsync(ServiceConfigurationContext context)
    {
        RemoveAutoRegistrationsShadowedByManualOnes(context.Services);
        return Task.CompletedTask;
    }

    private void RemoveAutoRegistrationsShadowedByManualOnes(IServiceCollection services)
    {
        if (_autoRegisteredHandlers.Count == 0)
            return;

        var autoRegistered = new HashSet<ServiceDescriptor>(_autoRegisteredHandlers, ReferenceEqualityComparer.Instance);
        _autoRegisteredHandlers = [];

        var manuallyRegistered = new HashSet<(Type ServiceType, Type ImplementationType)>();
        foreach (var descriptor in services)
        {
            if (descriptor.IsKeyedService || autoRegistered.Contains(descriptor) || !IsEventHandlerService(descriptor.ServiceType))
                continue;

            var implementationType = descriptor.ImplementationType ?? descriptor.ImplementationInstance?.GetType();
            if (implementationType != null)
                manuallyRegistered.Add((descriptor.ServiceType, implementationType));
        }

        foreach (var descriptor in autoRegistered)
        {
            if (manuallyRegistered.Contains((descriptor.ServiceType, descriptor.ImplementationType!)))
                services.Remove(descriptor);
        }
    }

    private static bool IsEventHandlerService(Type serviceType)
        => serviceType.IsGenericType && serviceType.GetGenericTypeDefinition() == typeof(IEventHandler<>);

    /// <summary>
    /// 启动自证：<c>EventBus:Type</c> 选了分布式传输时，容器里必须真的有 <see cref="IDistributedEventBus"/>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 只有传输模块自己读 <c>Type</c>，模块没加载时它们根本不运行。配了 <c>Type=RabbitMQ</c> 却漏了
    /// <c>[DependsOn(typeof(RabbitMQEventBusModule))]</c> 的服务：集成事件经 <c>PublishEventAsync</c> 落到本地总线，
    /// 发布成功、本服务的处理器照常执行、健康检查是绿的，只是别的服务永远收不到。
    /// 与缓存、对象存储同口径：配了却没加载就抛异常并指名要加载的包，绝不回退。
    /// </para>
    /// <para>
    /// 只问「注册了没有」，不解析也不看实现类型：解析会构造总线并连接代理，代理暂时不可达不该让这道检查失败；
    /// 自定义传输是正当做法。
    /// </para>
    /// </remarks>
    public override Task OnApplicationInitializationAsync(ApplicationInitializationContext context)
    {
        var type = context.ServiceProvider.GetService<IOptions<EventBusOptions>>()?.Value.Type;
        string? package = null;
        string? moduleName = null;
        if (string.Equals(type, "RabbitMQ", StringComparison.OrdinalIgnoreCase))
        {
            (package, moduleName) = ("Tnzi.RabbitMQ", "RabbitMQEventBusModule");
        }
        else if (string.Equals(type, "Kafka", StringComparison.OrdinalIgnoreCase))
        {
            (package, moduleName) = ("Tnzi.Kafka", "KafkaEventBusModule");
        }

        if (package == null)
            return Task.CompletedTask;

        var isService = context.ServiceProvider.GetService<IServiceProviderIsService>();
        if (isService == null || isService.IsService(typeof(IDistributedEventBus)))
            return Task.CompletedTask;

        throw new ConfigurationException(
            "EventBus:Type",
            $"EventBus:Type is '{type}' but no IDistributedEventBus is registered, so integration events would only be " +
            "delivered inside this process while publishing, logs and health checks all look healthy. " +
            $"Load the {package} package ([DependsOn(typeof({moduleName}))]) or set EventBus:Type to 'Local'.");
    }

    /// <summary>
    /// 自动注册事件处理器
    /// </summary>
    protected internal void AutoRegisterEventHandlers(IServiceCollection services, EventBusOptions options, ILogger logger)
    {
        try
        {
            // 扫描所有事件处理器类型
            var handlerTypes = ScanEventHandlerTypes(options, logger);

            if (handlerTypes.Count == 0)
            {
                logger.LogDebug("No event handlers found for auto-registration");
                return;
            }

            // 按事件类型分组并排序
            var groupedHandlers = handlerTypes
                .GroupBy(h => h.EventType)
                .ToList();

            int registeredCount = 0;
            int skippedCount = 0;

            foreach (var group in groupedHandlers)
            {
                var eventType = group.Key;
                var handlers = group
                    .OrderBy(h => h.HandlerType.GetCustomAttribute<EventHandlerOrderAttribute>()?.Order ?? 0)
                    .ToList();

                foreach (var handlerInfo in handlers)
                {
                    // 检查是否已手动注册
                    if (IsAlreadyRegistered(services, handlerInfo.HandlerType, eventType))
                    {
                        logger.LogDebug("Skipping handler {HandlerType} for event {EventType} (already manually registered)",
                            handlerInfo.HandlerType.Name, eventType.Name);
                        skippedCount++;
                        continue;
                    }

                    // 验证处理器类型（检查 IgnoreEventHandler 特性）
                    // 注意：接口检查已在 ScanEventHandlerTypes 中完成，这里只检查特性
                    if (handlerInfo.HandlerType.HasAttribute<IgnoreEventHandlerAttribute>())
                    {
                        logger.LogDebug("Skipping handler {HandlerType} (marked with [IgnoreEventHandler])",
                            handlerInfo.HandlerType.Name);
                        skippedCount++;
                        continue;
                    }

                    // 检查是否为框架程序集，输出警告
                    var handlerAssemblyName = handlerInfo.HandlerType.Assembly.GetName().Name ?? "";
                    if (handlerAssemblyName.StartsWith("Tnzi", StringComparison.OrdinalIgnoreCase))
                    {
                        logger.LogWarning(
                            "Auto-registering event handler {HandlerType} from framework assembly {AssemblyName}. " +
                            "Framework modules should use manual registration in ConfigureServicesAsync. " +
                            "Consider adding [IgnoreEventHandler] attribute or manually registering this handler.",
                            handlerInfo.HandlerType.Name, handlerAssemblyName);
                    }

                    // 获取生命周期
                    var lifetime = GetHandlerLifetime(handlerInfo.HandlerType, options);

                    // 注册到 DI 容器
                    // 使用 TryAddEnumerable 以支持同一事件类型的多个处理器实现
                    // TryAddEnumerable 会检查是否已存在相同的 ServiceType + ImplementationType，避免重复注册
                    var handlerServiceType = typeof(IEventHandler<>).MakeGenericType(eventType);
                    var descriptor = new ServiceDescriptor(handlerServiceType, handlerInfo.HandlerType, lifetime);
                    var countBefore = services.Count;
                    services.TryAddEnumerable(descriptor);
                    if (services.Count > countBefore)
                        _autoRegisteredHandlers.Add(descriptor);

                    registeredCount++;
                    logger.LogDebug("Auto-registered handler {HandlerType} for event {EventType} with lifetime {Lifetime}",
                        handlerInfo.HandlerType.Name, eventType.Name, lifetime);
                }
            }

            logger.LogInformation("Auto-registered {Count} event handler(s), skipped {Skipped} handler(s)",
                registeredCount, skippedCount);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occurred while auto-registering event handlers");
            throw;
        }
    }

    /// <summary>
    /// 扫描程序集获取所有事件处理器类型
    /// </summary>
    private List<(Type HandlerType, Type EventType)> ScanEventHandlerTypes(EventBusOptions options, ILogger logger)
    {
        var handlerTypes = new List<(Type HandlerType, Type EventType)>();
        var assemblies = GetTargetAssemblies(options, logger);

        foreach (var assembly in assemblies)
        {
            try
            {
                var types = assembly.GetExportedTypes()
                    .Where(t => t.IsClass && !t.IsAbstract && !t.IsGenericTypeDefinition)
                    .ToList();

                foreach (var type in types)
                {
                    // 查找实现的全部 IEventHandler<TEvent> 接口：
                    // 一个处理器类可以同时处理多种事件，只取第一个会让其余事件静默地无人订阅
                    foreach (var eventHandlerInterface in type.GetInterfaces())
                    {
                        if (!eventHandlerInterface.IsGenericType ||
                            eventHandlerInterface.GetGenericTypeDefinition() != typeof(IEventHandler<>))
                        {
                            continue;
                        }

                        var eventType = eventHandlerInterface.GetGenericArguments()[0];
                        handlerTypes.Add((type, eventType));
                    }
                }
            }
            catch (ReflectionTypeLoadException ex)
            {
                // 记录类型加载异常（某些类型可能无法加载）
                var assemblyName = assembly.GetName().Name ?? assembly.FullName ?? "Unknown";
                logger.LogWarning(ex,
                    "Failed to load some types from assembly {AssemblyName} while scanning for event handlers. " +
                    "Some event handlers may not be discovered.",
                    assemblyName);
                continue;
            }
            catch (Exception ex)
            {
                // 记录其他异常但不抛出，继续扫描其他程序集
                var assemblyName = assembly.GetName().Name ?? assembly.FullName ?? "Unknown";
                logger.LogWarning(ex,
                    "Failed to scan assembly {AssemblyName} for event handlers. " +
                    "This assembly will be skipped.",
                    assemblyName);
                continue;
            }
        }

        return handlerTypes;
    }

    /// <summary>
    /// 获取目标程序集列表
    /// </summary>
    private Assembly[] GetTargetAssemblies(EventBusOptions options, ILogger logger)
    {
        if (options.HandlerAssemblies.Count > 0)
        {
            // 扫描指定的程序集
            var assemblies = new List<Assembly>();
            var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies().ToList();

            foreach (var assemblyName in options.HandlerAssemblies)
            {
                var assembly = loadedAssemblies.FirstOrDefault(a =>
                {
                    var name = a.GetName().Name ?? "";
                    var fullName = a.FullName ?? "";
                    return name.Equals(assemblyName, StringComparison.OrdinalIgnoreCase) ||
                           fullName.Equals(assemblyName, StringComparison.OrdinalIgnoreCase) ||
                           fullName.StartsWith(assemblyName + ",", StringComparison.OrdinalIgnoreCase);
                });

                if (assembly != null)
                {
                    assemblies.Add(assembly);
                }
                else
                {
                    // 输出警告：指定的程序集未找到
                    // 注意：此时 logger 可能是 NullLogger，但至少尝试记录警告
                    logger.LogWarning(
                        "Assembly '{AssemblyName}' specified in EventBus.HandlerAssemblies was not found. " +
                        "Make sure the assembly is loaded before EventBusModule.ConfigureServicesAsync is called.",
                        assemblyName);
                }
            }

            return assemblies.ToArray();
        }
        else
        {
            // 扫描所有已加载的非系统程序集。
            // 框架程序集（Tnzi.*）默认被排除：框架模块的处理器一律经 `AddEventHandler<TEvent, THandler>()`
            // 手动注册，其中一部分按配置有条件地注册，扫描会把模块刻意没注册的那些也加进来。
            // （与手动注册重复的自动注册由 PostConfigureServicesAsync 撤下，不会让同一处理器执行两遍。）
            // 用 `EventBus.ExcludeFrameworkAssemblies = false` 可恢复旧行为。
            return AppDomain.CurrentDomain.GetAssemblies()
                .Where(a =>
                {
                    var name = a.GetName().Name ?? "";
                    if (a.IsDynamic ||
                        name.StartsWith("System", StringComparison.OrdinalIgnoreCase) ||
                        name.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase) ||
                        name.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                    if (options.ExcludeFrameworkAssemblies &&
                        name.StartsWith("Tnzi", StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                    return true;
                })
                .ToArray();
        }
    }

    /// <summary>
    /// 获取处理器生命周期（当前统一取配置值，handlerType 保留给按类型定制的扩展点）
    /// </summary>
    private ServiceLifetime GetHandlerLifetime(Type handlerType, EventBusOptions options)
    {
        return options.DefaultHandlerLifetime;
    }

    /// <summary>
    /// 检查处理器是否已手动注册
    /// </summary>
    private bool IsAlreadyRegistered(IServiceCollection services, Type handlerType, Type eventType)
    {
        var handlerServiceType = typeof(IEventHandler<>).MakeGenericType(eventType);

        return services.Any(descriptor =>
        {
            if (descriptor.ServiceType != handlerServiceType)
                return false;

            // 检查 ImplementationType
            if (descriptor.ImplementationType == handlerType)
                return true;

            // 检查 ImplementationInstance
            if (descriptor.ImplementationInstance != null &&
                descriptor.ImplementationInstance.GetType() == handlerType)
                return true;

            // 注意：ImplementationFactory 无法静态推断返回类型
            // 但 TryAddEnumerable 会在运行时检查重复，所以这里不需要检查工厂方法
            // 如果使用工厂方法注册，TryAddEnumerable 会正确处理重复问题

            return false;
        });
    }
}