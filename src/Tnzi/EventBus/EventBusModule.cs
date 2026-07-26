
namespace Tnzi.EventBus;

/// <summary>
/// 事件总线模块
/// 配置路径：EventBus
/// </summary>
[DependsOn(typeof(CachingModule))]
public class EventBusModule : TnziInfrastructureModule
{
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
                    services.TryAddEnumerable(descriptor);

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
                    // 查找实现的 IEventHandler<TEvent> 接口
                    var eventHandlerInterface = type.GetInterfaces()
                        .FirstOrDefault(i => i.IsGenericType &&
                                           i.GetGenericTypeDefinition() == typeof(IEventHandler<>));

                    if (eventHandlerInterface != null)
                    {
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
            // 框架程序集（Tnzi.*）默认被排除：framework rule 要求所有
            // Tnzi.* handler 必须 manual register via `AddEventHandler<TEvent, THandler>()`。
            // 若同时被 auto-scan 扫到，由于 EventBusModule 比应用模块先跑，
            // IsAlreadyRegistered 在 auto-scan 时还看不到后续的手动注册，
            // 结果同一 handler 在 DI 出现两条 descriptor → 一次事件被调用两次。
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