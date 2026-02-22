
namespace Tnzi.EFCore.Internal;

/// <summary>
/// 程序集扫描工具类
/// 提供统一的程序集加载、扫描和类型过滤功能
/// </summary>
public static class AssemblyScanner
{
    // 系统程序集前缀（用于优化扫描性能）
    private static readonly string[] SystemPrefixes =
    [
        "System", "Microsoft", "netstandard", "mscorlib", "WindowsBase",
        "PresentationFramework", "PresentationCore", "UIAutomation",
        "Accessibility", "runtime."
    ];

    // 常见第三方库前缀（用于优化扫描性能）
    private static readonly string[] ThirdPartyPrefixes =
    [
        "Newtonsoft", "Npgsql", "MySql", "Pomelo", "sqlite", "Oracle",
        "MongoDB", "Redis", "StackExchange", "RabbitMQ", "MassTransit",
        "Serilog", "NLog", "log4net", "AutoMapper", "Mapster", "FluentValidation",
        "Polly", "Quartz", "Hangfire", "MediatR", "OpenTelemetry",
        "Swashbuckle", "NSwag", "Grpc", "IdentityModel", "IdentityServer",
        "xunit", "NUnit", "Moq", "FluentAssertions", "Bogus", "FakeItEasy",
        "Dapper", "Azure", "AWS", "Google", "Yarp", "HealthChecks", "Humanizer",
        "CsvHelper", "ClosedXML", "EPPlus", "ImageSharp", "SkiaSharp", "QRCoder"
    ];

    // 缓存所有前缀（提升性能）
    private static readonly string[] AllExcludedPrefixes;

    static AssemblyScanner()
    {
        AllExcludedPrefixes = [..SystemPrefixes, ..ThirdPartyPrefixes];
    }

    /// <summary>
    /// 判断是否为系统程序集或常见第三方库
    /// </summary>
    /// <param name="assemblyName">程序集名称</param>
    /// <returns>如果是系统或第三方程序集返回 true</returns>
    public static bool IsSystemOrThirdPartyAssembly(string assemblyName)
    {
        if (string.IsNullOrEmpty(assemblyName))
            return true;

        foreach (var prefix in AllExcludedPrefixes)
        {
            if (assemblyName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// 获取所有应用程序的程序集（排除系统和第三方库）
    /// </summary>
    /// <returns>应用程序的程序集集合</returns>
    public static IEnumerable<Assembly> GetApplicationAssemblies()
    {
        return AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Where(a => !IsSystemOrThirdPartyAssembly(a.GetName().Name ?? string.Empty));
    }

    /// <summary>
    /// 获取所有已加载程序集（包含系统和第三方库）
    /// </summary>
    /// <returns>所有已加载的程序集集合</returns>
    public static IEnumerable<Assembly> GetAllLoadedAssemblies()
    {
        return AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location));
    }

    /// <summary>
    /// 加载入口程序集及其所有引用的程序集
    /// </summary>
    /// <param name="logger">日志记录器（可选）</param>
    /// <returns>加载的程序集数量</returns>
    public static int LoadEntryAssemblyReferences(ILogger? logger = null)
    {
        var entryAssembly = Assembly.GetEntryAssembly();
        if (entryAssembly == null)
        {
            logger?.LogDebug("Entry assembly is null, skipping reference loading");
            return 0;
        }

        return LoadAssemblyReferences(entryAssembly, logger);
    }

    /// <summary>
    /// 以指定程序集为根，加载其所有引用的程序集（排除系统和第三方程序集）
    /// </summary>
    /// <param name="rootAssembly">根程序集</param>
    /// <param name="logger">日志记录器（可选）</param>
    /// <returns>新加载的程序集数量</returns>
    public static int LoadAssemblyReferences(Assembly rootAssembly, ILogger? logger = null)
    {
        if (rootAssembly == null) return 0;

        try
        {
            var loadedAssemblies = new HashSet<string>(
                AppDomain.CurrentDomain.GetAssemblies()
                    .Where(a => !a.IsDynamic)
                    .Select(a => a.GetName().Name ?? string.Empty),
                StringComparer.OrdinalIgnoreCase);

            logger?.LogDebug("Loading references starting from: {Assembly}", 
                rootAssembly.GetName().Name);

            var assembliesToCheck = new Queue<Assembly>();
            assembliesToCheck.Enqueue(rootAssembly);

            var processedCount = 0;
            
            while (assembliesToCheck.Count > 0)
            {
                var assembly = assembliesToCheck.Dequeue();

                foreach (var referencedName in assembly.GetReferencedAssemblies())
                {
                    if (referencedName.Name == null)
                        continue;

                    // 跳过系统程序集和常见第三方库
                    if (IsSystemOrThirdPartyAssembly(referencedName.Name))
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
            
            if (processedCount > 0)
            {
                logger?.LogDebug("Loaded {Count} additional assemblies from references of {Root}", 
                    processedCount, rootAssembly.GetName().Name);
            }
            return processedCount;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to load assembly references starting from {Root}", 
                rootAssembly.GetName().Name);
            return 0;
        }
    }

    /// <summary>
    /// 加载指定目录下的所有应用程序程序集
    /// </summary>
    /// <param name="directory">目录路径</param>
    /// <param name="logger">日志记录器（可选）</param>
    /// <returns>加载的程序集数量</returns>
    public static int LoadAllAssembliesFromProjectBinDirectory(string directory, ILogger? logger = null)
    {
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            return 0;

        try
        {
            var loadedAssemblies = new HashSet<string>(
                AppDomain.CurrentDomain.GetAssemblies()
                    .Where(a => !a.IsDynamic)
                    .Select(a => a.GetName().Name ?? string.Empty),
                StringComparer.OrdinalIgnoreCase);

            var files = Directory.GetFiles(directory, "*.dll", SearchOption.TopDirectoryOnly);
            var processedCount = 0;

            foreach (var file in files)
            {
                try
                {
                    var fileName = Path.GetFileNameWithoutExtension(file);
                    
                    // 跳过已加载的
                    if (loadedAssemblies.Contains(fileName))
                        continue;

                    // 跳过系统和第三方程序集
                    if (IsSystemOrThirdPartyAssembly(fileName))
                        continue;

                    var assembly = Assembly.LoadFrom(file);
                    loadedAssemblies.Add(fileName);
                    processedCount++;
                }
                catch
                {
                    // 忽略加载失败的文件
                }
            }

            if (processedCount > 0)
            {
                logger?.LogDebug("Loaded {Count} assemblies from directory: {Directory}", 
                    processedCount, directory);
            }
            return processedCount;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to load assemblies from directory: {Directory}", directory);
            return 0;
        }
    }

    /// <summary>
    /// 扫描程序集中实现指定接口的所有类型
    /// </summary>
    /// <typeparam name="TInterface">要扫描的接口类型</typeparam>
    /// <param name="assemblies">要扫描的程序集</param>
    /// <param name="logger">日志记录器（可选）</param>
    /// <returns>实现指定接口的类型集合</returns>
    public static Type[] FindTypesImplementing<TInterface>(
        IEnumerable<Assembly>? assemblies = null,
        ILogger? logger = null)
    {
        var interfaceType = typeof(TInterface);
        var types = new List<Type>();
        var scannedAssemblies = new HashSet<Assembly>();

        assemblies ??= GetApplicationAssemblies();

        foreach (var assembly in assemblies)
        {
            if (scannedAssemblies.Contains(assembly))
                continue;

            scannedAssemblies.Add(assembly);

            try
            {
                var assemblyTypes = assembly.GetTypes()
                    .Where(t => interfaceType.IsAssignableFrom(t)
                        && t.IsClass
                        && !t.IsAbstract
                        && !t.IsNestedPrivate)
                    .ToArray();

                if (assemblyTypes.Length > 0)
                {
                    types.AddRange(assemblyTypes);
                    logger?.LogDebug("Found {Count} {Interface} types in assembly {Assembly}",
                        assemblyTypes.Length, interfaceType.Name, assembly.GetName().Name);
                }
            }
            catch (ReflectionTypeLoadException ex)
            {
                logger?.LogWarning(ex, "Failed to load types from assembly: {Assembly}", assembly.FullName);
                // 尝试加载可加载的类型
                if (ex.Types != null)
                {
                    foreach (var type in ex.Types)
                    {
                        if (type != null && interfaceType.IsAssignableFrom(type) && type.IsClass && !type.IsAbstract)
                        {
                            types.Add(type);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Error scanning assembly {Assembly} for {Interface} types", 
                    assembly.FullName, interfaceType.Name);
            }
        }

        return types.Distinct().ToArray();
    }

    /// <summary>
    /// 安全地获取程序集中的所有类型（处理加载异常）
    /// </summary>
    /// <param name="assembly">程序集</param>
    /// <param name="logger">日志记录器（可选）</param>
    /// <returns>程序集中的类型集合</returns>
    public static Type[] GetTypesSafely(Assembly assembly, ILogger? logger = null)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            logger?.LogDebug(ex, "Failed to load some types from assembly: {Assembly}", assembly.FullName);
            return ex.Types.Where(t => t != null).Cast<Type>().ToArray();
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "Failed to load types from assembly: {Assembly}", assembly.FullName);
            return [];
        }
    }
}