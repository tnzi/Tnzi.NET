

namespace Tnzi.EFCore;

/// <summary>
/// 设计时 DbContext 工厂基类，简化迁移命令的使用
/// </summary>
/// <typeparam name="TDbContext">DbContext 类型</typeparam>
public abstract class DesignTimeDbContextFactoryBase<TDbContext> : IDesignTimeDbContextFactory<TDbContext>
    where TDbContext : DbContext
{

    /// <summary>
    /// 创建 DbContext 实例
    /// </summary>
    public TDbContext CreateDbContext(string[] args)
    {
        // Allow subclasses to inject design-time-only static state (e.g. migrations assembly,
        // module container) before the context is built. Runs in both add/update commands.
        OnBeforeCreate(args);

        // Find configuration file path
        var configPath = FindConfigurationPath();
        if (string.IsNullOrEmpty(configPath))
        {
            throw new InvalidOperationException(
                "Cannot find appsettings.json file.\n" +
                "Please ensure:\n" +
                "1. Execute migration command in the startup project directory (e.g., Tnzi.Api)\n" +
                "2. Or use the -StartupProject parameter to specify the startup project\n" +
                "3. Or execute command in a directory containing appsettings.json");
        }

        // Build configuration (include environment variables so ${VAR} placeholders can be resolved)
        var configuration = new ConfigurationBuilder()
            .SetBasePath(configPath)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables()
            .Build();

        // Read from Database configuration
        var databaseOptions = configuration.GetSection("Database").Get<DatabaseOptions>();

        if (databaseOptions?.DbContexts == null || databaseOptions.DbContexts.Count == 0)
        {
            throw new InvalidOperationException(
                "No 'Database' configuration found in appsettings.json.\n" +
                "Please add 'Database' configuration section with your DbContext settings.");
        }

        // Find matching DbContext configuration
        var dbContextTypeName = typeof(TDbContext).FullName;
        var dbContextTypeSimpleName = typeof(TDbContext).Name; // e.g., "MusicDbContext"

        // 匹配策略：
        // 1. DbContextType 不为空：精确匹配 DbContextType
        // 2. DbContextType 为空：通过 Name 匹配（支持 "Default" 或类型名前缀）
        // 3. 如果只有一个配置，直接使用（简化场景）
        var config = databaseOptions.DbContexts.FirstOrDefault(c =>
        {
            // 策略1：DbContextType 精确匹配
            if (!string.IsNullOrEmpty(c.DbContextType))
            {
                return c.DbContextType.Contains(dbContextTypeSimpleName) ||
                       c.DbContextType == dbContextTypeName ||
                       c.DbContextType.EndsWith($".{dbContextTypeSimpleName}, {typeof(TDbContext).Assembly.GetName().Name}");
            }

            // 策略2：通过 Name 匹配（简化配置场景）
            // 如果 Name 是 "Default"，或者类型名包含 Name（如 Name="Music", Type="MusicDbContext"）
            if (!string.IsNullOrEmpty(c.Name))
            {
                var name = c.Name;
                // 如果 Name 是 "Default"，假设是主 DbContext，匹配第一个或主配置
                if (string.Equals(name, "Default", StringComparison.OrdinalIgnoreCase))
                {
                    // 如果只有一个配置，或者这是主配置，则匹配
                    return databaseOptions.DbContexts.Count == 1 ||
                           (databaseOptions.PrimaryDbContext != null &&
                            string.Equals(databaseOptions.PrimaryDbContext, name, StringComparison.OrdinalIgnoreCase));
                }

                // 检查类型名是否包含 Name（如 Name="Music" 匹配 "MusicDbContext"）
                if (dbContextTypeSimpleName.Contains(name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        });

        // 如果仍未找到，且只有一个配置，直接使用它（简化场景）
        if (config == null && databaseOptions.DbContexts.Count == 1)
        {
            config = databaseOptions.DbContexts[0];
        }

        if (config == null || string.IsNullOrEmpty(config.ConnectionString))
        {
            throw new InvalidOperationException(
                $"Cannot find DbContext configuration for '{typeof(TDbContext).FullName}' in 'Database' section.\n" +
                $"Please add a configuration entry with DbContextType matching your DbContext type, or use Name='Default' for simplified configuration.");
        }

        // Expand ${VAR} / $(VAR) placeholders in connection string (environment variables + configuration)
        var effectiveConnectionString = config.GetEffectiveConnectionString(configuration);

        // 如果 Provider 未指定或为默认值，尝试自动检测（使用展开后的连接字符串）
        if (config.Provider == DatabaseProvider.SqlServer && !string.IsNullOrWhiteSpace(effectiveConnectionString))
        {
            if (Internal.DatabaseProviderDetector.TryDetectFromConnectionString(effectiveConnectionString, out var detectedProvider, out _, null))
            {
                if (detectedProvider != DatabaseProvider.SqlServer)
                {
                    config.Provider = detectedProvider;
                }
            }
        }

        // Scan TnziApplicationModule and set DesignTimeModuleContainer
        // This enables TableNamePrefixConfiguration to access module TableNamePrefix during migrations
        var modules = ScanApplicationModules();
        TableNamePrefixConfiguration.DesignTimeModuleContainer =
            modules.Count > 0 ? new ModuleContainer(modules) : null;

        // Build options using provider from configuration
        var optionsBuilder = new DbContextOptionsBuilder<TDbContext>();
        ConfigureOptionsByProvider(optionsBuilder, effectiveConnectionString, config.Provider);

        // Create design-time CurrentUser
        var currentUser = new DesignTimeCurrentUser();

        // Create DbContext instance
        return CreateDbContextInstance(optionsBuilder.Options, currentUser);
    }

    /// <summary>
    /// 扫描应用模块，按 DependsOn 依赖树过滤，仅包含被依赖的模块（与 Controller 发现逻辑一致）
    /// 无法确定启动模块或解析失败时回退到扫描所有已发现的模块
    /// </summary>
    private List<IModuleDescriptor> ScanApplicationModules()
    {
        var modules = new List<IModuleDescriptor>();

        try
        {
            // 加载引用程序集
            Internal.AssemblyScanner.LoadEntryAssemblyReferences();

            var contextAssembly = typeof(TDbContext).Assembly;
            Internal.AssemblyScanner.LoadAssemblyReferences(contextAssembly);

            var assemblyDir = Path.GetDirectoryName(contextAssembly.Location);
            if (!string.IsNullOrEmpty(assemblyDir))
            {
                Internal.AssemblyScanner.LoadAllAssembliesFromProjectBinDirectory(assemblyDir);
            }

            // 发现所有 TnziApplicationModule 类型
            var allModuleTypes = Internal.AssemblyScanner.FindTypesImplementing<TnziApplicationModule>();

            // 1. 查找启动模块：优先 DbContext 程序集，其次 EntryAssembly
            var startupModuleType = Internal.DesignTimeModuleDiscovery.FindStartupModuleType(contextAssembly, allModuleTypes);

            if (startupModuleType != null)
            {
                // 2. 按 DependsOn 递归构建依赖树（容错：解析失败的依赖跳过）
                var modulesDict = new Dictionary<Type, ModuleDescriptor>();
                var visiting = new HashSet<Type>();
                DesignTimeModuleDiscovery.FillDependsOnTree(startupModuleType, modulesDict, visiting);

                if (modulesDict.Count > 0)
                {
                    modules = modulesDict.Values.OrderBy(m => m.Type.FullName).Cast<IModuleDescriptor>().ToList();
                    Console.WriteLine($"[DesignTime] Discovered {modules.Count} modules (DependsOn filtered) for DbContext '{typeof(TDbContext).Name}'.");
                }
            }

            // 3. 回退：无启动模块或依赖树为空时，使用所有已发现的模块
            if (modules.Count == 0)
            {
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
                        // 忽略无法创建的模块类型
                    }
                }

                if (modules.Count > 0)
                {
                    Console.WriteLine($"[DesignTime] Discovered {modules.Count} modules (fallback: all) for DbContext '{typeof(TDbContext).Name}'.");
                }
            }
        }
        catch
        {
            // 扫描失败
        }

        return modules;
    }







    /// <summary>
    /// 根据提供者类型配置数据库选项（内部使用）
    /// 使用反射调用扩展方法，避免直接依赖提供者包
    /// </summary>
    private void ConfigureOptionsByProvider(DbContextOptionsBuilder<TDbContext> builder, string connectionString, DatabaseProvider provider)
    {
        // 使用反射调用扩展方法，避免直接依赖提供者包
        var assemblyName = provider switch
        {
            DatabaseProvider.SqlServer => "Microsoft.EntityFrameworkCore.SqlServer",
            DatabaseProvider.PostgreSQL => "Npgsql.EntityFrameworkCore.PostgreSQL",
            DatabaseProvider.MySql => "Pomelo.EntityFrameworkCore.MySql",
            DatabaseProvider.Sqlite => "Microsoft.EntityFrameworkCore.Sqlite",
            _ => throw new NotSupportedException($"Database provider {provider} is not supported.")
        };

        var methodName = provider switch
        {
            DatabaseProvider.SqlServer => "UseSqlServer",
            DatabaseProvider.PostgreSQL => "UseNpgsql",
            DatabaseProvider.MySql => "UseMySql",
            DatabaseProvider.Sqlite => "UseSqlite",
            _ => throw new NotSupportedException($"Database provider {provider} is not supported.")
        };

        // 首先尝试从已加载的程序集中查找
        var assembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == assemblyName);

        // 如果未找到，尝试多种方式加载程序集
        if (assembly == null)
        {
            // 方法1: 尝试通过 AssemblyName 加载（.NET 会自动解析版本）
            try
            {
                assembly = Assembly.Load(new AssemblyName(assemblyName));
            }
            catch
            {
                // 方法2: 尝试查找程序集文件并加载
                assembly = TryLoadAssemblyFromPath(assemblyName);
            }

            // 如果仍然失败，抛出详细错误
            if (assembly == null)
            {
                throw new InvalidOperationException(
                    $"Database provider assembly '{assemblyName}' is not loaded. " +
                    $"Please ensure the EF Core provider package is installed. " +
                    $"Try running 'dotnet restore' and 'dotnet build' before executing migration commands.");
            }
        }

        // 确保程序集的所有类型都被加载
        Type[]? allTypes = null;
        try
        {
            allTypes = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            // 使用可加载的类型继续查找
            allTypes = ex.Types.Where(t => t != null).Cast<Type>().ToArray();
        }

        if (allTypes == null)
        {
            allTypes = Array.Empty<Type>();
        }

        // 直接在所有类型中查找扩展方法
        // 扩展方法接受 DbContextOptionsBuilder（非泛型）作为第一个参数
        MethodInfo? extensionMethod = null;
        foreach (var type in allTypes)
        {
            try
            {
                if (type == null || !type.IsClass || !type.IsSealed || type.IsGenericType)
                    continue;

                var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .Where(m => m.Name == methodName)
                    .ToList();

                if (methods.Count == 0)
                    continue;

                // 优先查找 2 个参数的重载: (DbContextOptionsBuilder, string)
                extensionMethod = methods.FirstOrDefault(m =>
                {
                    var parameters = m.GetParameters();
                    if (parameters.Length != 2)
                        return false;

                    var firstParam = parameters[0].ParameterType;
                    var secondParam = parameters[1].ParameterType;

                    // 扩展方法第一个参数是 DbContextOptionsBuilder（非泛型）
                    // DbContextOptionsBuilder<TDbContext> 可以隐式转换为 DbContextOptionsBuilder
                    return firstParam == typeof(DbContextOptionsBuilder) &&
                           secondParam == typeof(string);
                });

                // 如果找不到 2 个参数的重载，尝试查找 3 个参数的重载（第三个参数是 Action）
                if (extensionMethod == null)
                {
                    extensionMethod = methods.FirstOrDefault(m =>
                    {
                        var parameters = m.GetParameters();
                        if (parameters.Length != 3)
                            return false;

                        var firstParam = parameters[0].ParameterType;
                        var secondParam = parameters[1].ParameterType;
                        var thirdParam = parameters[2].ParameterType;

                        return firstParam == typeof(DbContextOptionsBuilder) &&
                               secondParam == typeof(string) &&
                               thirdParam.IsGenericType &&
                               thirdParam.GetGenericTypeDefinition() == typeof(Action<>);
                    });
                }

                if (extensionMethod != null)
                    break;
            }
            catch
            {
                // 忽略单个类型的错误，继续查找
            }
        }

        // MySql 特殊处理：如果未找到标准方法，尝试查找需要 ServerVersion 的重载
        if (extensionMethod == null && provider == DatabaseProvider.MySql)
        {
            foreach (var type in allTypes)
            {
                try
                {
                    if (type == null || !type.IsClass || !type.IsSealed || type.IsGenericType)
                        continue;

                    var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                        .Where(m => m.Name == methodName)
                        .ToList();

                    if (methods.Count == 0)
                        continue;

                    // 查找需要 ServerVersion 的方法
                    var serverVersionMethod = methods.FirstOrDefault(m =>
                    {
                        var parameters = m.GetParameters();
                        if (parameters.Length != 3)
                            return false;

                        var firstParam = parameters[0].ParameterType;
                        var secondParam = parameters[1].ParameterType;
                        var thirdParam = parameters[2].ParameterType;

                        return firstParam == typeof(DbContextOptionsBuilder) &&
                               secondParam == typeof(string) &&
                               thirdParam.Name == "ServerVersion";
                    });

                    if (serverVersionMethod != null)
                    {
                        // 尝试自动检测 ServerVersion
                        var serverVersionType = assembly.GetTypes()
                            .FirstOrDefault(t => t.Name == "ServerVersion");
                        if (serverVersionType != null)
                        {
                            var autoDetectMethod = serverVersionType.GetMethod("AutoDetect",
                                new[] { typeof(string) });
                            if (autoDetectMethod != null)
                            {
                                var serverVersion = autoDetectMethod.Invoke(null, new object[] { connectionString });
                                if (serverVersion != null)
                                {
                                    // 直接调用并返回
                                    serverVersionMethod.Invoke(null, new object[] { builder, connectionString, serverVersion });
                                    return;
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // 忽略错误，继续查找
                }
            }
        }

        if (extensionMethod == null)
        {
            throw new InvalidOperationException(
                $"Extension method '{methodName}' not found in '{assemblyName}'. " +
                $"Please ensure the package version is compatible.");
        }

        // 调用方法
        var parameters = extensionMethod.GetParameters();
        if (parameters.Length == 2)
        {
            // 标准调用：builder, connectionString
            // DbContextOptionsBuilder<TDbContext> 可以隐式转换为 DbContextOptionsBuilder
            extensionMethod.Invoke(null, new object[] { builder, connectionString });
        }
        else if (parameters.Length == 3)
        {
            // 检查第三个参数类型
            var thirdParam = parameters[2].ParameterType;
            if (thirdParam.IsGenericType && thirdParam.GetGenericTypeDefinition() == typeof(Action<>))
            {
                // 调用：builder, connectionString, action (第三个参数可以是 null)
                extensionMethod.Invoke(null, new object[] { builder, connectionString, null! });
            }
            else
            {
                throw new InvalidOperationException(
                    $"Extension method '{methodName}' has unsupported third parameter type '{thirdParam.Name}'.");
            }
        }
        else
        {
            throw new InvalidOperationException(
                $"Extension method '{methodName}' has {parameters.Length} parameters, which is not supported.");
        }
    }

    /// <summary>
    /// 尝试从文件路径加载程序集（用于设计时场景）
    /// </summary>
    private Assembly? TryLoadAssemblyFromPath(string assemblyName)
    {
        try
        {
            // 获取当前执行程序集的位置
            var currentAssemblyLocation = typeof(DesignTimeDbContextFactoryBase<>).Assembly.Location;
            if (!string.IsNullOrEmpty(currentAssemblyLocation))
            {
                var currentDir = Path.GetDirectoryName(currentAssemblyLocation);
                if (currentDir != null)
                {
                    // 方法1: 在同一目录中查找
                    var assemblyFile = Path.Combine(currentDir, $"{assemblyName}.dll");
                    if (File.Exists(assemblyFile))
                    {
                        return Assembly.LoadFrom(assemblyFile);
                    }
                }
            }

            // 方法2: 查找项目输出目录
            var configPath = FindConfigurationPath();
            if (!string.IsNullOrEmpty(configPath))
            {
                // 尝试在 bin/Debug 或 bin/Release 目录中查找
                var binPaths = new[]
                {
                    Path.Combine(configPath, "bin", "Debug", "net10.0", $"{assemblyName}.dll"),
                    Path.Combine(configPath, "bin", "Release", "net10.0", $"{assemblyName}.dll"),
                    Path.Combine(configPath, "bin", "Debug", $"{assemblyName}.dll"),
                    Path.Combine(configPath, "bin", "Release", $"{assemblyName}.dll"),
                };

                foreach (var binPath in binPaths)
                {
                    if (File.Exists(binPath))
                    {
                        return Assembly.LoadFrom(binPath);
                    }
                }
            }

            // 方法3: 查找 NuGet 包缓存目录
            var nugetPaths = new List<string>();

            // 从环境变量获取 NuGet 包路径
            var nugetPackagesEnv = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
            if (!string.IsNullOrEmpty(nugetPackagesEnv))
            {
                nugetPaths.Add(nugetPackagesEnv);
            }

            // 从用户目录查找
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(userProfile))
            {
                nugetPaths.Add(Path.Combine(userProfile, ".nuget", "packages"));
            }

            // 从项目目录查找 NuGet 包
            if (!string.IsNullOrEmpty(configPath))
            {
                var projectDir = Directory.GetParent(configPath)?.Parent?.FullName;
                if (!string.IsNullOrEmpty(projectDir))
                {
                    nugetPaths.Add(Path.Combine(projectDir, "packages"));
                }
            }

            foreach (var nugetBasePath in nugetPaths.Where(Directory.Exists))
            {
                var nugetPath = Path.Combine(nugetBasePath, assemblyName.ToLower());
                if (Directory.Exists(nugetPath))
                {
                    // 查找最新版本目录
                    var versionDirs = Directory.GetDirectories(nugetPath)
                        .OrderByDescending(d => d)
                        .ToList();

                    foreach (var versionDir in versionDirs)
                    {
                        var libPaths = new[]
                        {
                            Path.Combine(versionDir, "lib", "net10.0", $"{assemblyName}.dll"),
                            Path.Combine(versionDir, "lib", "net9.0", $"{assemblyName}.dll"),
                            Path.Combine(versionDir, "lib", "net8.0", $"{assemblyName}.dll"),
                            Path.Combine(versionDir, "lib", "netstandard2.1", $"{assemblyName}.dll"),
                            Path.Combine(versionDir, "lib", "netstandard2.0", $"{assemblyName}.dll"),
                        };

                        foreach (var libPath in libPaths)
                        {
                            if (File.Exists(libPath))
                            {
                                return Assembly.LoadFrom(libPath);
                            }
                        }
                    }
                }
            }
        }
        catch
        {
            // 忽略所有异常，返回 null
        }

        return null;
    }

    /// <summary>
    /// 在构建 DbContext 之前调用的钩子（默认空实现）。
    /// 子类可重写以设置仅设计时需要的静态状态，例如迁移文件所在程序集。
    /// </summary>
    /// <param name="args">设计时命令参数</param>
    protected virtual void OnBeforeCreate(string[] args)
    {
    }

    /// <summary>
    /// 创建 DbContext 实例（子类可覆盖以自定义创建逻辑）
    /// </summary>
    protected virtual TDbContext CreateDbContextInstance(DbContextOptions<TDbContext> options, ICurrentUser currentUser)
    {
        // Try to find constructor with ICurrentUser parameter
        var constructor = typeof(TDbContext).GetConstructor(
            new[] { typeof(DbContextOptions<TDbContext>), typeof(ICurrentUser) });

        if (constructor != null)
        {
            return (TDbContext)constructor.Invoke(new object[] { options, currentUser });
        }

        // If not found, try constructor with only DbContextOptions parameter
        var optionsOnlyConstructor = typeof(TDbContext).GetConstructor(
            new[] { typeof(DbContextOptions<TDbContext>) });

        if (optionsOnlyConstructor != null)
        {
            return (TDbContext)optionsOnlyConstructor.Invoke(new object[] { options });
        }

        throw new InvalidOperationException(
            $"Cannot create instance of {typeof(TDbContext).Name}.\n" +
            $"Please ensure {typeof(TDbContext).Name} has one of the following constructors:\n" +
            $"1. {typeof(TDbContext).Name}(DbContextOptions<{typeof(TDbContext).Name}> options, ICurrentUser currentUser)\n" +
            $"2. {typeof(TDbContext).Name}(DbContextOptions<{typeof(TDbContext).Name}> options)");
    }

    /// <summary>
    /// 查找配置文件路径
    /// </summary>
    protected virtual string? FindConfigurationPath()
    {
        var currentDir = Directory.GetCurrentDirectory();

        // Strategy 1: Prioritize common Web project directories (*.Api, *.Web, *.WebApi)
        // These directories are usually at the same level or in parent directories
        var commonPaths = new[]
        {
            Path.Combine(currentDir, "..", ".."),       // From Tnzi/Data/ or Tnzi/Modules/Blog/Data/ to solution root
            Path.Combine(currentDir, ".."),             // Parent directory
            currentDir                                  // Current directory
        };

        foreach (var basePath in commonPaths)
        {
            var fullPath = Path.GetFullPath(basePath);

            // Look for appsettings.json directly in current path
            var configFile = Path.Combine(fullPath, "appsettings.json");
            if (File.Exists(configFile))
            {
                return fullPath;
            }

            // Look for Web project directories in current path
            if (Directory.Exists(fullPath))
            {
                try
                {
                    var webProjects = Directory.GetDirectories(fullPath, "*.Api", SearchOption.TopDirectoryOnly)
                        .Concat(Directory.GetDirectories(fullPath, "*.Web", SearchOption.TopDirectoryOnly))
                        .Concat(Directory.GetDirectories(fullPath, "*.WebApi", SearchOption.TopDirectoryOnly));

                    foreach (var webProject in webProjects)
                    {
                        configFile = Path.Combine(webProject, "appsettings.json");
                        if (File.Exists(configFile))
                        {
                            return webProject;
                        }
                    }
                }
                catch
                {
                    // Ignore directory access errors, continue searching
                }
            }
        }

        // Strategy 2: Search upward from current directory (max 5 levels)
        var searchDir = currentDir;
        var maxDepth = 5;
        var depth = 0;

        while (searchDir != null && depth < maxDepth)
        {
            var configFile = Path.Combine(searchDir, "appsettings.json");
            if (File.Exists(configFile))
            {
                return searchDir;
            }

            var parent = Directory.GetParent(searchDir);
            searchDir = parent?.FullName;
            depth++;
        }

        return null;
    }
}