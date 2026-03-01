
namespace Tnzi.Application;

/// <summary>
/// 应用服务基类
/// 支持延迟加载服务，减少不必要的依赖注入
/// 所有延迟加载的服务属性使用 Lazy&lt;T&gt; 确保线程安全
/// </summary>
[StableApi(Since = "0.1.0")]
public abstract class ApplicationService : IApplicationService
{
    private readonly Lazy<ICurrentUser?> _currentUser;
    private readonly Lazy<IPermissionChecker?> _permissionChecker;
    private readonly Lazy<IUnitOfWorkManager?> _unitOfWorkManager;
    private readonly Lazy<IEventBus?> _eventBus;
    private readonly Lazy<IEventStore?> _eventStore;
    private readonly Lazy<IScopedContext?> _scopedContext;
    private readonly Lazy<IPostCommitActionQueue?> _postCommitActionQueue;
    private readonly Lazy<TimeProvider> _timeProvider;
    private readonly Lazy<ILogger> _logger;

    /// <summary>
    /// 服务提供者
    /// </summary>
    protected IServiceProvider? ServiceProvider { get; }

    /// <summary>
    /// 推荐构造函数：使用 IServiceProvider 延迟加载服务
    /// </summary>
    /// <param name="serviceProvider">服务提供者</param>
    protected ApplicationService(IServiceProvider serviceProvider)
    {
        ServiceProvider = Check.NotNull(serviceProvider);

        // 使用 Lazy<T> 确保线程安全的延迟初始化
        // LazyThreadSafetyMode.ExecutionAndPublication 保证只初始化一次
        _currentUser = new Lazy<ICurrentUser?>(() => ServiceProvider?.GetService<ICurrentUser>(), LazyThreadSafetyMode.ExecutionAndPublication);
        _permissionChecker = new Lazy<IPermissionChecker?>(() => ServiceProvider?.GetService<IPermissionChecker>(), LazyThreadSafetyMode.ExecutionAndPublication);
        _unitOfWorkManager = new Lazy<IUnitOfWorkManager?>(() => ServiceProvider?.GetService<IUnitOfWorkManager>(), LazyThreadSafetyMode.ExecutionAndPublication);
        _eventBus = new Lazy<IEventBus?>(() => ServiceProvider?.GetService<IEventBus>(), LazyThreadSafetyMode.ExecutionAndPublication);
        _eventStore = new Lazy<IEventStore?>(() => ServiceProvider?.GetService<IEventStore>(), LazyThreadSafetyMode.ExecutionAndPublication);
        _scopedContext = new Lazy<IScopedContext?>(() => ServiceProvider?.GetService<IScopedContext>(), LazyThreadSafetyMode.ExecutionAndPublication);
        _postCommitActionQueue = new Lazy<IPostCommitActionQueue?>(() => ServiceProvider?.GetService<IPostCommitActionQueue>(), LazyThreadSafetyMode.ExecutionAndPublication);
        _timeProvider = new Lazy<TimeProvider>(() => serviceProvider.GetRequiredService<TimeProvider>(), LazyThreadSafetyMode.ExecutionAndPublication);
        _logger = new Lazy<ILogger>(() =>
        {
            if (ServiceProvider == null)
                throw new InvalidOperationException($"ServiceProvider is not available in {GetType().Name}");
            return ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(GetType());
        }, LazyThreadSafetyMode.ExecutionAndPublication);
    }


    /// <summary>
    /// 当前用户服务（延迟加载，线程安全）
    /// </summary>
    protected ICurrentUser? CurrentUser => _currentUser.Value;

    /// <summary>
    /// 获取当前用户服务（必需），如果未注册则抛出异常
    /// </summary>
    protected ICurrentUser GetRequiredCurrentUser()
    {
        return _currentUser.Value
            ?? throw new InvalidOperationException($"Service {nameof(ICurrentUser)} is not registered. Please ensure the Identity module is loaded.");
    }

    /// <summary>
    /// 权限检查器（延迟加载，线程安全）
    /// </summary>
    protected IPermissionChecker? PermissionChecker => _permissionChecker.Value;

    /// <summary>
    /// 获取权限检查器（必需），如果未注册则抛出异常
    /// </summary>
    protected IPermissionChecker GetRequiredPermissionChecker()
    {
        return _permissionChecker.Value
            ?? throw new InvalidOperationException($"Service {nameof(IPermissionChecker)} is not registered. Please ensure the Authorization module is loaded.");
    }

    /// <summary>
    /// 工作单元管理器（延迟加载，线程安全）
    /// </summary>
    protected IUnitOfWorkManager? UnitOfWorkManager => _unitOfWorkManager.Value;

    /// <summary>
    /// 获取工作单元管理器（必需），如果未注册则抛出异常
    /// </summary>
    protected IUnitOfWorkManager GetRequiredUnitOfWorkManager()
    {
        return _unitOfWorkManager.Value
            ?? throw new InvalidOperationException($"Service {nameof(IUnitOfWorkManager)} is not registered. Please ensure the EFCore module is loaded.");
    }

    /// <summary>
    /// 事件总线（延迟加载，线程安全）
    /// </summary>
    protected IEventBus? EventBus => _eventBus.Value;

    /// <summary>
    /// 获取事件总线（必需），如果未注册则抛出异常
    /// </summary>
    protected IEventBus GetRequiredEventBus()
    {
        return _eventBus.Value
            ?? throw new InvalidOperationException($"Service {nameof(IEventBus)} is not registered. Please ensure the EventBus module is loaded.");
    }

    /// <summary>
    /// 事件存储（延迟加载，线程安全）
    /// 当 Outbox 模式启用时可用，用于将集成事件持久化到 Outbox 表
    /// </summary>
    protected IEventStore? EventStore => _eventStore.Value;

    /// <summary>
    /// 发布事件（事务感知，支持 Outbox 模式）
    /// 对于 IIntegrationEvent：优先通过 IEventStore 写入 Outbox，由后台服务可靠投递
    /// 对于其他事件：在事务中时延迟到提交后发布，不在事务中时立即发布
    /// </summary>
    protected async Task PublishEventAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : class, IEvent
    {
        // 对 IIntegrationEvent 优先写入 Outbox，确保事务一致性
        if (@event is IIntegrationEvent && EventStore != null)
        {
            var eventType = @event.GetType().AssemblyQualifiedName ?? @event.GetType().FullName ?? @event.GetType().Name;
            await EventStore.SaveEventAsync(@event, eventType, cancellationToken);
            return;
        }

        var eventBus = EventBus;
        if (eventBus == null) return;

        if (UnitOfWorkManager?.IsEnabledTransaction == true && PostCommitActionQueue != null)
        {
            PostCommitActionQueue.Enqueue(ct => eventBus.PublishAsync(@event, ct));
            return;
        }

        await eventBus.PublishAsync(@event, cancellationToken);
    }

    /// <summary>
    /// 作用域上下文（延迟加载，线程安全）
    /// </summary>
    protected IScopedContext? ScopedContext => _scopedContext.Value;

    /// <summary>
    /// 获取作用域上下文（必需），如果未注册则抛出异常
    /// </summary>
    protected IScopedContext GetRequiredScopedContext()
    {
        return _scopedContext.Value
            ?? throw new InvalidOperationException($"Service {nameof(IScopedContext)} is not registered.");
    }

    /// <summary>
    /// 事务提交后操作队列（延迟加载，线程安全）
    /// </summary>
    protected IPostCommitActionQueue? PostCommitActionQueue => _postCommitActionQueue.Value;

    /// <summary>
    /// 时间提供者（延迟加载，线程安全）
    /// 用于获取可测试的时间戳，替代 DateTime.UtcNow
    /// </summary>
    protected TimeProvider TimeProvider => _timeProvider.Value;

    /// <summary>
    /// 日志记录器（延迟加载，线程安全）
    /// </summary>
    protected ILogger Logger => _logger.Value;

    /// <summary>
    /// 服务名称（从类型名称推断）
    /// 用于日志记录和诊断，返回实现类的名称（不含命名空间）
    /// </summary>
    /// <returns>服务名称，例如 "UserService"</returns>
    public virtual string? ServiceName => GetType().Name;

    /// <summary>
    /// 模块名称（从命名空间推断）
    /// 用于日志记录和诊断，从命名空间中提取模块名称
    /// </summary>
    /// <returns>模块名称，例如 "Identity"（从命名空间 Tnzi.Identity.Services 推断），如果无法推断则返回 null</returns>
    public virtual string? ModuleName
    {
        get
        {
            var ns = GetType().Namespace;
            if (string.IsNullOrEmpty(ns))
                return null;

            // 从命名空间提取模块名（例如：Tnzi.Identity.Services -> Identity）
            var parts = ns.Split('.');
            if (parts.Length >= 2 && parts[0] == "Tnzi")
                return parts[1];

            return null;
        }
    }

    /// <summary>
    /// 获取必需的服务，如果未注册则抛出异常
    /// </summary>
    /// <typeparam name="T">服务类型</typeparam>
    /// <returns>服务实例</returns>
    /// <exception cref="InvalidOperationException">服务未注册时抛出</exception>
    protected T GetRequiredService<T>() where T : notnull
    {
        if (ServiceProvider == null)
            throw new InvalidOperationException($"ServiceProvider is not available in {GetType().Name}");

        var service = ServiceProvider.GetService<T>();
        if (service == null)
            throw new InvalidOperationException($"Service {typeof(T).Name} is not registered. Please ensure the required module is loaded.");

        return service;
    }

    /// <summary>
    /// 检查服务是否可用
    /// </summary>
    /// <typeparam name="T">服务类型</typeparam>
    /// <returns>服务是否可用</returns>
    protected bool IsServiceAvailable<T>() where T : class
    {
        return ServiceProvider?.GetService<T>() != null;
    }

    /// <summary>
    /// 检查权限
    /// </summary>
    protected virtual async Task CheckPermissionAsync(string permissionName)
    {
        if (PermissionChecker != null)
            await PermissionChecker.CheckAsync(permissionName);
    }

    /// <summary>
    /// 检查是否有任意一个权限
    /// </summary>
    protected virtual async Task<bool> IsGrantedAnyAsync(params string[] permissionNames)
    {
        return PermissionChecker != null && await PermissionChecker.IsGrantedAnyAsync(permissionNames);
    }

    /// <summary>
    /// 检查是否有全部权限
    /// </summary>
    protected virtual async Task<bool> IsGrantedAllAsync(params string[] permissionNames)
    {
        return PermissionChecker != null && await PermissionChecker.IsGrantedAllAsync(permissionNames);
    }

    /// <summary>
    /// 开始工作单元
    /// </summary>
    protected virtual async Task BeginUnitOfWorkAsync(CancellationToken cancellationToken = default)
    {
        if (UnitOfWorkManager != null)
            await UnitOfWorkManager.BeginTransactionAsync(cancellationToken);
    }

    /// <summary>
    /// 在工作单元中执行操作（不返回结果）
    /// 如果工作单元管理器可用，则启用事务并在操作完成后自动提交，发生异常时自动回滚
    /// 如果工作单元管理器不可用，则直接执行操作（无事务）
    /// </summary>
    /// <param name="action">要执行的异步操作</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <exception cref="ArgumentNullException">当 action 为 null 时抛出</exception>
    protected virtual async Task ExecuteInUnitOfWorkAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken = default)
    {
        Check.NotNull(action);

        // 如果没有工作单元管理器，直接执行逻辑
        if (UnitOfWorkManager == null)
        {
            await action(cancellationToken);
            return;
        }

        UnitOfWorkManager.EnableTransaction();
        try
        {
            await action(cancellationToken);
            await UnitOfWorkManager.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await UnitOfWorkManager.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// 在工作单元中执行操作并返回结果
    /// 如果工作单元管理器可用，则启用事务并在操作完成后自动提交，发生异常时自动回滚
    /// 如果工作单元管理器不可用，则直接执行操作（无事务）
    /// </summary>
    /// <typeparam name="TResult">结果类型</typeparam>
    /// <param name="func">要执行的异步操作</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>操作结果</returns>
    /// <exception cref="ArgumentNullException">当 func 为 null 时抛出</exception>
    protected virtual async Task<TResult> ExecuteInUnitOfWorkAsync<TResult>(Func<CancellationToken, Task<TResult>> func, CancellationToken cancellationToken = default)
    {
        Check.NotNull(func);

        // 如果没有工作单元管理器，直接执行逻辑
        if (UnitOfWorkManager == null)
        {
            return await func(cancellationToken);
        }

        UnitOfWorkManager.EnableTransaction();
        try
        {
            var result = await func(cancellationToken);
            await UnitOfWorkManager.CommitTransactionAsync(cancellationToken);
            return result;
        }
        catch
        {
            await UnitOfWorkManager.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// 检查当前用户是否拥有任意一个权限，若无权限则抛出异常
    /// 如果权限检查器不可用，将抛出 ForbiddenException 异常
    /// 如果用户不拥有任何指定的权限，将抛出 ForbiddenException 异常
    /// </summary>
    /// <param name="permissionNames">权限名称集合，如果为空或 null 则不进行任何检查</param>
    /// <exception cref="ForbiddenException">当权限检查器不可用或用户不拥有任何指定权限时抛出</exception>
    protected virtual async Task RequireAnyPermissionAsync(params string[] permissionNames)
    {
        if (permissionNames == null || permissionNames.Length == 0)
        {
            return;
        }

        if (PermissionChecker == null)
        {
            throw new ForbiddenException(
                "Permission checker is not available. Please ensure the Authorization module is loaded.",
                ErrorCodes.FORBIDDEN);
        }

        var granted = await PermissionChecker.IsGrantedAnyAsync(permissionNames);
        if (!granted)
        {
            throw new ForbiddenException(
                $"Permission denied. Required any of: {string.Join(",", permissionNames)}",
                ErrorCodes.FORBIDDEN);
        }
    }

    /// <summary>
    /// 检查当前用户是否拥有全部指定权限，若无权限则抛出异常
    /// 如果权限检查器不可用，将抛出 ForbiddenException 异常
    /// 如果用户不拥有全部指定的权限，将抛出 ForbiddenException 异常
    /// </summary>
    /// <param name="permissionNames">权限名称集合，如果为空或 null 则不进行任何检查</param>
    /// <exception cref="ForbiddenException">当权限检查器不可用或用户不拥有全部指定权限时抛出</exception>
    protected virtual async Task RequireAllPermissionsAsync(params string[] permissionNames)
    {
        if (permissionNames == null || permissionNames.Length == 0)
        {
            return;
        }

        if (PermissionChecker == null)
        {
            throw new ForbiddenException(
                "Permission checker is not available. Please ensure the Authorization module is loaded.",
                ErrorCodes.FORBIDDEN);
        }

        var granted = await PermissionChecker.IsGrantedAllAsync(permissionNames);
        if (!granted)
        {
            throw new ForbiddenException(
                $"Permission denied. Required all of: {string.Join(",", permissionNames)}",
                ErrorCodes.FORBIDDEN);
        }
    }

    /// <summary>
    /// 创建成功结果（无数据）
    /// </summary>
    /// <param name="message">成功消息</param>
    protected Result Ok(string? message = null) => Result.Success(message);

    /// <summary>
    /// 创建成功结果（带数据）
    /// </summary>
    /// <typeparam name="T">数据类型</typeparam>
    /// <param name="data">数据</param>
    /// <param name="message">成功消息</param>
    /// <param name="code">HTTP 状态码（可选，默认 200）</param>
    protected Result<T> Ok<T>(T data, string? message = null, int? code = null) => Result<T>.Success(data, message, code);

    /// <summary>
    /// 创建失败结果（无数据）
    /// </summary>
    /// <param name="message">错误消息</param>
    /// <param name="code">HTTP 状态码</param>
    /// <param name="errorCode">业务错误代码</param>
    /// <param name="errorDetails">错误详情</param>
    protected Result Fail(string message, int code = 400, string? errorCode = null, object? errorDetails = null)
        => Result.Failure(message, code, errorCode, errorDetails);

    /// <summary>
    /// 创建失败结果（带数据）
    /// </summary>
    /// <typeparam name="T">数据类型</typeparam>
    /// <param name="message">错误消息</param>
    /// <param name="code">HTTP 状态码</param>
    /// <param name="errorCode">业务错误代码</param>
    /// <param name="errorDetails">错误详情</param>
    protected Result<T> Fail<T>(string message, int code = 400, string? errorCode = null, object? errorDetails = null)
        => Result<T>.Failure(message, code, errorCode, errorDetails);

    /// <summary>
    /// 记录信息级日志
    /// </summary>
    protected void LogInformation(string message, params object[] args) => Logger.LogInformation(message, args);

    /// <summary>
    /// 记录警告级日志
    /// </summary>
    protected void LogWarning(string message, params object[] args) => Logger.LogWarning(message, args);

    /// <summary>
    /// 记录错误级日志
    /// </summary>
    protected void LogError(string message, params object[] args) => Logger.LogError(message, args);

    // 对象映射请直接使用Tnzi.Mapster.MapperExtensions扩展方法
    // 示例: var dto = entity.MapTo<EntityDto>();
}