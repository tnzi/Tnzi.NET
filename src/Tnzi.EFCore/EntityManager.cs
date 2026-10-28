
namespace Tnzi.EFCore;

/// <summary>
/// 实体管理器
/// </summary>
public class EntityManager : IEntityManager
{
    private readonly ConcurrentDictionary<Type, IEntityRegister[]> _entityRegistersDict
        = new ConcurrentDictionary<Type, IEntityRegister[]>();
    private readonly ILogger _logger;
    private readonly IServiceProvider? _serviceProvider;
    private volatile bool _initialized;
    private readonly object _initLock = new();

    // AddTnziDbContext 登记的上下文与其中的主上下文（判定不了为 null），Initialize 时从容器读一次：
    // 设计期回退路径给的是一个用完即释放的临时容器，不能留到之后再读。
    private IReadOnlyList<RegisteredDbContext> _registrations = [];
    private Type? _primaryDbContextType;

    /// <summary>
    /// 初始化一个<see cref="EntityManager"/>类型的新实例
    /// </summary>
    public EntityManager(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _logger = serviceProvider.GetLogger<EntityManager>();
    }

    /// <summary>
    /// 初始化实体类型注册
    /// </summary>
    public virtual void Initialize()
    {
        if (_initialized)
        {
            _logger.LogDebug("EntityManager has been initialized, skipped");
            return;
        }

        lock (_initLock)
        {
            if (_initialized) return;

            // 关键：加载入口程序集及其所有引用的程序集
            // 这确保了应用程序的实体配置类能被发现
            Internal.AssemblyScanner.LoadEntryAssemblyReferences(_logger);

            // 查找并创建实体注册实例
            DiscoverRegisteredEntities();
            CaptureDbContextRegistrations();

            _initialized = true;
        }
    }

    /// <summary>读取 <c>AddTnziDbContext</c> 的登记，判定主上下文（规则见 <see cref="PrimaryDbContextResolver"/>）。</summary>
    private void CaptureDbContextRegistrations()
    {
        if (_serviceProvider == null)
        {
            return;
        }

        try
        {
            _registrations = _serviceProvider.GetServices<RegisteredDbContext>().ToList();
            _primaryDbContextType = PrimaryDbContextResolver.ResolvePrimary(_registrations);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read the registered DbContext types; the primary DbContext cannot be determined");
        }
    }

    /// <summary>
    /// 发现并注册实体
    /// </summary>
    private void DiscoverRegisteredEntities()
    {
        // 扫描所有程序集中实现了 IEntityRegister 的类型
        var types = FindEntityRegisterTypes();

        if (types.Length == 0)
        {
            _logger.LogWarning("No IEntityRegister types found in loaded assemblies");
            return;
        }

        // 创建实体映射类的实例
        var registers = types
            .Select(type =>
            {
                try
                {
                    return Activator.CreateInstance(type) as IEntityRegister;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to create instance of IEntityRegister type: {Type}", type.FullName);
                    return null;
                }
            })
            .Where(r => r != null)
            .ToList();

        // 按 DbContextType 分组
        var dict = _entityRegistersDict;
        // 注意：null 表示默认/主 DbContext，但对于不同的实际 DbContext 类型，我们需要特殊处理
        // 这里我们按实际的 DbContextType 分组，null 的会在 GetEntityRegisters 时特殊处理
        var groups = registers
            .Where(r => r != null)
            .GroupBy(r => r!.DbContextType ?? typeof(object)) // 使用 object 作为 null 的占位符
            .ToList();

        dict.Clear();
        foreach (var group in groups)
        {
            var key = group.Key;
            dict[key] = group.ToArray()!;
        }

        // 记录日志
        foreach (var item in dict)
        {
            var contextName = item.Key == typeof(object) ? "DefaultDbContext" : item.Key.Name;
            _logger.LogDebug("DbContext {DbContextType} registered {Count} entities", contextName, item.Value.Length);
        }

        if (dict.Count == 0)
        {
            _logger.LogWarning(
                "No entity registers found. This may indicate a problem with assembly scanning or entity configuration discovery. " +
                "Ensure entity configuration classes inherit from EntityTypeConfigurationBase<TEntity, TKey>.");
        }
    }

    /// <summary>
    /// 查找所有实现了 IEntityRegister 的类型
    /// </summary>
    private Type[] FindEntityRegisterTypes()
    {
        // 插件系统发现的程序集
        var pluginAssemblies = new List<Assembly>();
        if (_serviceProvider != null)
        {
            try
            {
                var moduleContainer = _serviceProvider.GetService<IModuleContainer>();
                if (moduleContainer != null)
                {
                    pluginAssemblies.AddRange(moduleContainer.Modules.Select(m => m.Assembly));
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to get IModuleContainer from ServiceProvider");
            }
        }

        // 确定要扫描的程序集
        // - 有 IModuleContainer 时：仅扫描 DependsOn 依赖树内的模块程序集（设计时由 DesignTimeModuleContainer 提供）
        // - 无 IModuleContainer 时：使用 GetApplicationAssemblies() 作为回退
        IEnumerable<Assembly>? assembliesToScan = null;

        if (pluginAssemblies.Count > 0)
        {
            // 仅扫描模块程序集，尊重 DependsOn 依赖树过滤（与 Controller 发现逻辑一致）
            // 启动模块及其依赖已包含应用程序程序集（如 StartupModule 所在程序集）
            assembliesToScan = pluginAssemblies;
        }
        // 如果 pluginAssemblies 为空，传入 null 让 FindTypesImplementing 使用默认的 GetApplicationAssemblies()

        var types = Internal.AssemblyScanner.FindTypesImplementing<IEntityRegister>(assembliesToScan, _logger);

        return types.ToArray();
    }

    /// <summary>
    /// 获取指定上下文类型的实体配置注册信息
    /// </summary>
    /// <remarks>
    /// 主次按本实例从容器读到的 <c>AddTnziDbContext</c> 登记判定（规则见 <see cref="PrimaryDbContextResolver"/>）；
    /// 判定不了时按旧规则：有显式绑定就只用显式绑定，没有就用未指定上下文的那一组。
    /// </remarks>
    public virtual IEntityRegister[] GetEntityRegisters(Type dbContextType)
    {
        EnsureInitialized();
        return GetEntityRegisters(dbContextType, PrimaryDbContextResolver.IsPrimary(dbContextType, _registrations));
    }

    /// <summary>
    /// 按主次获取指定上下文的实体配置注册：主上下文 = 显式绑定它的 ∪ 未指定上下文的；次上下文 = 只有显式绑定它的。
    /// </summary>
    /// <remarks>
    /// ★★ 此前只有「精确命中就只返回显式绑定」一条规则，它不知道谁是主上下文：主上下文只要有一个配置写了
    /// <c>DbContextType =&gt; typeof(主上下文)</c>，所有未指定上下文的实体（全部框架模块实体）从模型里消失，
    /// 下一次 <c>migrations add</c> 生成整批 DropTable；反过来，没有显式绑定的次上下文拿到全部默认实体。
    /// <paramref name="isPrimary"/> 为 null（判定不了）时仍走那条旧规则。
    /// </remarks>
    public virtual IEntityRegister[] GetEntityRegisters(Type dbContextType, bool? isPrimary)
    {
        Check.NotNull(dbContextType);
        EnsureInitialized();

        var explicitRegisters = _entityRegistersDict.TryGetValue(dbContextType, out var bound) ? bound : [];

        // DbContextType 为 null 的注册在初始化时存在 typeof(object) 键下
        var defaultRegisters = _entityRegistersDict.TryGetValue(typeof(object), out var defaults) ? defaults : [];

        return isPrimary switch
        {
            true => [.. explicitRegisters, .. defaultRegisters],
            false => explicitRegisters,
            null => explicitRegisters.Length > 0 ? explicitRegisters : defaultRegisters
        };
    }

    /// <summary>
    /// 获取实体类所属的数据上下文类型
    /// </summary>
    /// <remarks>
    /// ★★ 未指定上下文的实体属于主上下文，返回主上下文类型。此前原样返回分组用的 <c>typeof(object)</c> 占位符：
    /// <c>IDapperExecutor</c> 解析 <c>System.Object</c> 即抛、审计保留期的硬删把它读成「未注册实体」、
    /// 存储的孤立引用校验解析不出上下文而一律判「仍存在」。判定不了主上下文、或实体的注册键不是 DbContext
    /// （Outbox 关闭时的占位）时抛异常，不再把占位符交出去。
    /// </remarks>
    public virtual Type GetDbContextTypeForEntity(Type entityType)
    {
        EnsureInitialized();

        var dict = _entityRegistersDict;
        if (dict.IsEmpty)
        {
            throw new InvalidOperationException(
                "No entity registers found. " +
                "Please ensure entity configuration classes inherit from EntityTypeConfigurationBase<TEntity, TKey>.");
        }

        // 精确匹配实体类型（不搜索基类继承链，避免误匹配）
        foreach (var item in dict)
        {
            if (!item.Value.Any(m => m.EntityType == entityType))
            {
                continue;
            }

            var dbContextType = item.Key == typeof(object)
                ? _primaryDbContextType ?? throw new InvalidOperationException(
                    $"Entity type {entityType.FullName} belongs to the primary DbContext, but the primary DbContext cannot be determined. " +
                    "Register it with services.AddTnziDbContext<TDbContext>(..., isPrimary: true) or configure it under 'Database:DbContexts'.")
                : item.Key;

            if (!typeof(DbContext).IsAssignableFrom(dbContextType))
            {
                throw new InvalidOperationException(
                    $"Entity type {entityType.FullName} is not mapped to any DbContext in the current configuration.");
            }

            _logger.LogDebug("Entity {EntityType} belongs to DbContext {DbContextType}", entityType.Name, dbContextType.Name);
            return dbContextType;
        }

        throw new InvalidOperationException(
            $"Unable to determine DbContext for entity type {entityType.FullName}. " +
            "Please ensure the entity has a configuration class that inherits from EntityTypeConfigurationBase<TEntity, TKey>.");
    }

    /// <summary>
    /// 获取所有已注册的实体类型
    /// </summary>
    public Type[] GetAllEntityTypes()
    {
        if (!_initialized)
        {
            return Array.Empty<Type>();
        }

        return _entityRegistersDict.Values
            .SelectMany(registers => registers.Select(r => r.EntityType))
            .Distinct()
            .ToArray();
    }

    /// <summary>
    /// 获取所有已注册的 DbContext 类型（不包括占位符类型 typeof(object)）
    /// </summary>
    public Type[] GetAllDbContextTypes()
    {
        if (!_initialized)
        {
            return Array.Empty<Type>();
        }

        return _entityRegistersDict.Keys
            .Where(key => key != typeof(object) && typeof(DbContext).IsAssignableFrom(key))
            .ToArray();
    }

    private void EnsureInitialized()
    {
        if (!_initialized)
        {
            throw new InvalidOperationException(
                "EntityManager has not been initialized. " +
                "Please ensure EntityManager.Initialize() is called before using it.");
        }
    }
}