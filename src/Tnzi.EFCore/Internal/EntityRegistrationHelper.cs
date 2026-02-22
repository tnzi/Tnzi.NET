
namespace Tnzi.EFCore.Internal;

/// <summary>
/// 实体注册辅助类
/// 负责实体自动发现、注册、批量配置（TableNamePrefix）和程序集加载
/// </summary>
internal static class EntityRegistrationHelper
{
    private static bool _assembliesLoadedForEntityDiscovery = false;
    private static readonly object _assembliesLoadLock = new object();

    #region 实体注册

    /// <summary>
    /// 注册实体到模型（原 OnModelCreating 的核心逻辑）
    /// </summary>
    public static void RegisterEntities(DbContext dbContext, ModelBuilder modelBuilder, Action<ModelBuilder>? additionalConfigurations = null)
    {
        // 1. 调用额外的配置（如 Identity 的基础表名配置）
        additionalConfigurations?.Invoke(modelBuilder);

        // 2. 自动注册实体
        var entityManager = GetEntityManager(dbContext);
        if (entityManager != null)
        {
            var contextType = dbContext.GetType();
            var registers = entityManager.GetEntityRegisters(contextType);
            var logger = DbContextServiceResolver.GetLogger(dbContext);

            foreach (var register in registers)
            {
                // 传递 DbContext 以便在实体配置中获取数据库提供者信息
                register.RegisterTo(modelBuilder, dbContext);
            }

            if (registers.Length == 0)
            {
                logger?.LogWarning(
                    "No entity registers found for DbContext {DbContextType}. " +
                    "This may indicate that entity configuration classes are not being discovered. " +
                    "Ensure EntityManager.Initialize() was called and entity configuration classes inherit from EntityTypeConfigurationBase<TEntity, TKey>.",
                    contextType.Name);
            }
        }
        else
        {
            var logger = DbContextServiceResolver.GetLogger(dbContext);
            logger?.LogWarning(
                "EntityManager is null for DbContext {DbContextType}. Entity configurations will not be automatically applied.",
                dbContext.GetType().Name);
        }

        // 3. 应用批量配置（如 TableNamePrefix）
        ApplyBatchConfigurations(dbContext, modelBuilder);
    }

    #endregion

    #region 批量配置

    /// <summary>
    /// 应用批量实体配置（如 TableNamePrefix）
    /// </summary>
    public static void ApplyBatchConfigurations(DbContext dbContext, ModelBuilder modelBuilder)
    {
        var entityTypes = modelBuilder.Model.GetEntityTypes().ToList();
        var moduleContainer = GetModuleContainerForBatchConfig(dbContext);
        var batchConfigurations = GetBatchConfigurations(dbContext, moduleContainer);
        var logger = DbContextServiceResolver.GetLogger(dbContext);

        if (batchConfigurations != null && batchConfigurations.Any())
        {
            foreach (var entityType in entityTypes)
            {
                foreach (var batchConfig in batchConfigurations)
                {
                    if (batchConfig is TableNamePrefixConfiguration tableNameConfig)
                    {
                        if (moduleContainer != null)
                        {
                            tableNameConfig.ConfigureWithModuleContainer(modelBuilder, entityType, moduleContainer);

                            // 记录调试信息，确认表名前缀已应用
                            var tableName = modelBuilder.Entity(entityType.ClrType).Metadata.GetTableName();
                            logger?.LogDebug("Applied table name prefix for entity {EntityType}, final table name: {TableName}",
                                entityType.ClrType.Name, tableName);
                        }
                        else
                        {
                            logger?.LogWarning("Cannot apply table name prefix for entity {EntityType}: IModuleContainer is null.",
                                entityType.ClrType.Name);
                            batchConfig.Configure(modelBuilder, entityType);
                        }
                    }
                    else
                    {
                        batchConfig.Configure(modelBuilder, entityType);
                    }
                }
            }
        }
        else if (moduleContainer != null)
        {
            var tableNameConfig = new TableNamePrefixConfiguration(moduleContainer);
            foreach (var entityType in entityTypes)
            {
                tableNameConfig.ConfigureWithModuleContainer(modelBuilder, entityType, moduleContainer);
            }
        }
    }

    /// <summary>
    /// 获取批量配置集合
    /// </summary>
    private static IEnumerable<IEntityBatchConfiguration>? GetBatchConfigurations(DbContext dbContext, IModuleContainer? moduleContainer)
    {
        var serviceProvider = DbContextServiceResolver.GetServiceProvider(dbContext);
        IEnumerable<IEntityBatchConfiguration>? configurations = null;

        if (serviceProvider != null)
        {
            configurations = serviceProvider.GetServices<IEntityBatchConfiguration>();
        }

        if (configurations == null || !configurations.Any())
        {
            try
            {
                configurations = dbContext.Database.GetService<IEnumerable<IEntityBatchConfiguration>>();
            }
            catch (Exception ex)
            {
                var logger = DbContextServiceResolver.GetLogger(dbContext);
                logger?.LogDebug(ex, "Failed to get IEntityBatchConfiguration from DbContext.Database");
            }
        }

        if (configurations != null && configurations.Any())
        {
            if (moduleContainer != null)
            {
                var configList = configurations.ToList();
                var index = configList.FindIndex(c => c is TableNamePrefixConfiguration);
                if (index >= 0)
                {
                    configList[index] = new TableNamePrefixConfiguration(moduleContainer);
                }
                return configList;
            }
            return configurations;
        }

        return new[] { new TableNamePrefixConfiguration(moduleContainer) };
    }

    /// <summary>
    /// 获取模块容器
    /// </summary>
    private static IModuleContainer? GetModuleContainerForBatchConfig(DbContext dbContext)
    {
        var serviceProvider = DbContextServiceResolver.GetServiceProvider(dbContext);
        IModuleContainer? moduleContainer = null;

        if (serviceProvider != null)
        {
            var application = serviceProvider.GetService<ITnziApplication>();
            if (application != null)
            {
                moduleContainer = new ModuleContainer(application.Modules);
            }
            else
            {
                moduleContainer = serviceProvider.GetService<IModuleContainer>();
            }
        }

        if (moduleContainer == null)
        {
            moduleContainer = TableNamePrefixConfiguration.DesignTimeModuleContainer;
        }

        if (moduleContainer == null)
        {
            try
            {
                var modules = ScanApplicationModulesForDesignTime(dbContext);
                if (modules.Any())
                {
                    moduleContainer = new ModuleContainer(modules);
                    TableNamePrefixConfiguration.DesignTimeModuleContainer ??= moduleContainer;
                }
            }
            catch (Exception ex)
            {
                var logger = DbContextServiceResolver.GetLogger(dbContext);
                logger?.LogDebug(ex, "Failed to scan application modules for design time");
            }
        }

        return moduleContainer;
    }

    #endregion

    #region 实体管理器获取

    /// <summary>
    /// 获取实体管理器
    /// </summary>
    public static IEntityManager? GetEntityManager(DbContext dbContext)
    {
        // 关键：无论从何处获取 EntityManager，都先确保程序集已加载
        // 这在设计时场景特别重要，因为需要扫描应用程序程序集中的实体配置类
        LoadReferencedAssembliesForEntityDiscovery(dbContext.GetType().Assembly);

        try
        {
            var entityManager = dbContext.Database.GetService<IEntityManager>();
            if (entityManager != null)
            {
                entityManager.Initialize();
                return entityManager;
            }
        }
        catch (Exception ex)
        {
            var logger = DbContextServiceResolver.GetLogger(dbContext);
            logger?.LogDebug(ex, "Failed to get IEntityManager from DbContext.Database");
        }

        try
        {
            var serviceCollection = new ServiceCollection();
            serviceCollection.AddLogging();

            var application = DbContextServiceResolver.GetServiceProvider(dbContext)?.GetService<ITnziApplication>();
            if (application != null)
            {
                serviceCollection.AddSingleton<IModuleContainer>(new ModuleContainer(application.Modules));
            }
            else
            {
                // 设计时：使用 DesignTimeDbContextFactoryBase 设置的 DesignTimeModuleContainer
                // 确保 EntityManager 仅扫描 DependsOn 依赖树内的模块实体
                var designTimeContainer = TableNamePrefixConfiguration.DesignTimeModuleContainer;
                if (designTimeContainer != null)
                {
                    serviceCollection.AddSingleton<IModuleContainer>(designTimeContainer);
                }
            }

            using var sp = serviceCollection.BuildServiceProvider();
            var manager = new EntityManager(sp);
            manager.Initialize();
            return manager;
        }
        catch (Exception ex)
        {
            var logger = DbContextServiceResolver.GetLogger(dbContext);
            logger?.LogWarning(ex, "Failed to create EntityManager fallback instance");
            return null;
        }
    }

    #endregion

    #region 程序集加载

    /// <summary>
    /// 加载引用程序集以便实体发现
    /// </summary>
    public static void LoadReferencedAssembliesForEntityDiscovery(Assembly? contextAssembly = null)
    {
        if (_assembliesLoadedForEntityDiscovery) return;
        lock (_assembliesLoadLock)
        {
            if (_assembliesLoadedForEntityDiscovery) return;

            var logger = GetLoggerForAssemblyLoading();

            // 在设计时，优先基于 DbContext 所在的程序集加载引用
            // 这样可以确保应用程序的实体配置类能被正确发现
            if (contextAssembly != null)
            {
                logger?.LogDebug("Loading assembly references from DbContext assembly: {Assembly}",
                    contextAssembly.GetName().Name);

                // 加载 DbContext 程序集的引用
                var loadedAssemblies = new HashSet<string>(
                    AppDomain.CurrentDomain.GetAssemblies()
                        .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                        .Select(a => a.GetName().Name ?? string.Empty),
                    StringComparer.OrdinalIgnoreCase);

                var assembliesToCheck = new Queue<Assembly>();
                assembliesToCheck.Enqueue(contextAssembly);

                var processedCount = 0;

                while (assembliesToCheck.Count > 0)
                {
                    var assembly = assembliesToCheck.Dequeue();

                    foreach (var referencedName in assembly.GetReferencedAssemblies())
                    {
                        if (referencedName.Name == null)
                            continue;

                        // 跳过系统程序集和常见第三方库
                        if (AssemblyScanner.IsSystemOrThirdPartyAssembly(referencedName.Name))
                            continue;

                        if (loadedAssemblies.Contains(referencedName.Name))
                            continue;

                        try
                        {
                            var loadedAssembly = Assembly.Load(referencedName);
                            loadedAssemblies.Add(referencedName.Name);
                            processedCount++;

                            if (!loadedAssembly.IsDynamic && !string.IsNullOrEmpty(loadedAssembly.Location))
                            {
                                assembliesToCheck.Enqueue(loadedAssembly);
                            }
                        }
                        catch (Exception ex)
                        {
                            logger?.LogDebug(ex, "Failed to load assembly: {Assembly}", referencedName.Name);
                        }
                    }
                }

                logger?.LogDebug("Loaded {Count} additional assemblies from DbContext assembly references", processedCount);

                // 设计时：从 bin 目录加载所有应用程序集（含 Hosting 的传递依赖）
                // 与 DesignTimeDbContextFactoryBase 一致，确保 Authorization/Notification/System 等模块被扫描
                var assemblyDir = System.IO.Path.GetDirectoryName(contextAssembly.Location);
                if (!string.IsNullOrEmpty(assemblyDir))
                {
                    AssemblyScanner.LoadAllAssembliesFromProjectBinDirectory(assemblyDir, logger);
                }
            }
            else
            {
                // 回退到入口程序集加载（运行时场景）
                AssemblyScanner.LoadEntryAssemblyReferences(logger);
            }

            _assembliesLoadedForEntityDiscovery = true;
        }
    }

    private static ILogger? GetLoggerForAssemblyLoading()
    {
        return Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
    }

    /// <summary>
    /// 设计时回退：扫描应用模块，按 DependsOn 依赖树过滤（与 DesignTimeDbContextFactoryBase 一致）
    /// </summary>
    private static List<IModuleDescriptor> ScanApplicationModulesForDesignTime(DbContext dbContext)
    {
        var modules = new List<IModuleDescriptor>();
        try
        {
            AssemblyScanner.LoadEntryAssemblyReferences();
            var contextAssembly = dbContext.GetType().Assembly;
            AssemblyScanner.LoadAssemblyReferences(contextAssembly);
            var assemblyDir = System.IO.Path.GetDirectoryName(contextAssembly.Location);
            if (!string.IsNullOrEmpty(assemblyDir))
            {
                AssemblyScanner.LoadAllAssembliesFromProjectBinDirectory(assemblyDir);
            }

            var allModuleTypes = AssemblyScanner.FindTypesImplementing<TnziApplicationModule>();
            var startupModuleType = DesignTimeModuleDiscovery.FindStartupModuleType(contextAssembly, allModuleTypes);

            if (startupModuleType != null)
            {
                var modulesDict = new Dictionary<Type, ModuleDescriptor>();
                var visiting = new HashSet<Type>();
                DesignTimeModuleDiscovery.FillDependsOnTree(startupModuleType, modulesDict, visiting);
                if (modulesDict.Count > 0)
                {
                    return modulesDict.Values.OrderBy(m => m.Type.FullName).Cast<IModuleDescriptor>().ToList();
                }
            }

            foreach (var moduleType in allModuleTypes)
            {
                try
                {
                    var module = Activator.CreateInstance(moduleType) as ITnziModule;
                    if (module != null)
                    {
                        modules.Add(new ModuleDescriptor(moduleType, module));
                    }
                }
                catch
                {
                    // 忽略无法创建模块实例的错误
                }
            }
        }
        catch
        {
            // 扫描模块时可能发生错误，返回已找到的模块
        }
        return modules;
    }

    #endregion
}
