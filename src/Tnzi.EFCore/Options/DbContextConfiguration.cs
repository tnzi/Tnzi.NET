namespace Tnzi.EFCore.Options;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Tnzi.EFCore;
using Tnzi.EFCore.Internal;

/// <summary>
/// DbContext 配置
/// </summary>
public class DbContextConfiguration
{
    // 缓存 DbContext 类型解析结果：Key = DbContextType 字符串 -> Type
    private static readonly ConcurrentDictionary<string, Type?> _typeCache = new();
    
    // 缓存自动发现的 DbContext 类型：Key = Name -> Type
    private static readonly ConcurrentDictionary<string, Type> _autoDiscoveryCache = new();
    /// <summary>
    /// DbContext 名称（用于标识和引用，如 "Default"、"Blog"）
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// DbContext 类型（完整类型名称，如 "Tnzi.Data.DefaultDbContext, Tnzi.Data"）
    /// </summary>
    public string DbContextType { get; set; } = string.Empty;

    /// <summary>
    /// 连接字符串（直接存储，不是引用 ConnectionStrings 节点）
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// 数据库提供者类型
    /// </summary>
    public DatabaseProvider Provider { get; set; }

    /// <summary>
    /// 连接池配置（可选）
    /// 用于配置数据库连接池参数，提升高并发场景下的性能
    /// 如果不配置，使用数据库驱动默认值
    /// </summary>
    public ConnectionPoolOptions? ConnectionPool { get; set; }

    /// <summary>
    /// 是否为该 DbContext 启用瞬时错误重试（retrying execution strategy）。默认 false。
    /// ⚠️ 启用后与框架 UnitOfWork 的手动事务互斥：UoW 的手动 BeginTransaction 会在运行时抛异常。
    /// 若该 DbContext 需要重试策略，应关闭其全局 UoW 事务，或改用 IExecutionStrategy.ExecuteAsync 显式包裹写操作。
    /// 详见 docs/modules/efcore.md「重试与 Execution Strategy」。SQLite 无重试策略，此项对 SQLite 无效。
    /// </summary>
    public bool EnableRetryOnFailure { get; set; }

    /// <summary>
    /// 最大重试次数。为 null 时使用 provider 默认值（EF Core 默认 6）。
    /// 仅当 <see cref="EnableRetryOnFailure"/> 为 true 时生效。
    /// </summary>
    public int? MaxRetryCount { get; set; }

    /// <summary>
    /// 数据库命令超时（秒）。为 null 时使用 provider 默认值。
    /// </summary>
    public int? CommandTimeout { get; set; }

    /// <summary>
    /// 构建传递给数据库提供者配置器的连接级选项（重试策略、命令超时）。
    /// </summary>
    public DbProviderConfigureOptions BuildProviderConfigureOptions() => new()
    {
        EnableRetryOnFailure = EnableRetryOnFailure,
        MaxRetryCount = MaxRetryCount,
        CommandTimeout = CommandTimeout
    };

    /// <summary>
    /// 获取最终的连接字符串（包含连接池配置和环境变量展开）
    /// </summary>
    /// <param name="configuration">配置对象（可选，用于环境变量和占位符展开）</param>
    /// <param name="logger">日志记录器（可选）</param>
    /// <returns>展开并合并连接池配置后的连接字符串</returns>
    /// <exception cref="InvalidOperationException">
    /// 展开后仍有占位符没解析出来。带字面量 <c>${VAR}</c> 的连接串没有任何正确用途，唯一的下游症状是一条
    /// 认证失败而日志里没有一句指向占位符，所以在这里直接拒绝并指名变量（失败方向关闭）。
    /// </exception>
    public string GetEffectiveConnectionString(IConfiguration? configuration = null, ILogger? logger = null)
    {
        var expandedConnectionString = ConnectionString;

        // 如果包含自定义占位符，进行展开
        // 注意：ASP.NET Core 配置系统已经支持环境变量替换（通过环境变量提供程序）
        // 这里主要是支持自定义占位符语法 ${VAR} 或 $(VAR)
        if (ConnectionStringExpander.ContainsPlaceholders(expandedConnectionString))
        {
            expandedConnectionString = ConnectionStringExpander.Expand(expandedConnectionString, configuration, logger);

            var unresolved = ConnectionStringExpander.GetPlaceholderVariables(expandedConnectionString);
            if (unresolved.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Connection string for DbContext '{Name}' still contains unresolved placeholder(s): {string.Join(", ", unresolved)}. " +
                    "Set the corresponding environment variable or configuration value; a connection string with a literal placeholder cannot be used. " +
                    "Design-time commands that never open a connection (migrations add / migrations script) accept any value for the variable.");
            }
        }

        // 应用连接池配置
        if (ConnectionPool == null || !ConnectionPool.HasConfiguration)
        {
            return expandedConnectionString;
        }

        return ConnectionStringBuilder.ApplyPoolingOptions(expandedConnectionString, ConnectionPool, Provider);
    }

    /// <summary>
    /// 获取 DbContext 类型（内部使用，带缓存）
    /// </summary>
    /// <remarks>
    /// <c>DbContextType</c> 为空时回退到启动期按 <c>Name</c> 自动发现的结果（若已发现）。
    /// 启动期的回填只写在 <c>EFCoreModule</c> 自己那份绑定实例上，其它地方重新绑定配置拿到的是一份
    /// <c>DbContextType</c> 仍为空的新实例 —— 没有这条回退，它们会把「只写 Name」的配置读成「没有上下文」。
    /// 只查缓存不触发扫描：发现该在启动期发生一次，这里不该悄悄再来一次。
    /// </remarks>
    internal Type? GetDbContextType()
    {
        if (string.IsNullOrEmpty(DbContextType))
        {
            return !string.IsNullOrEmpty(Name) && _autoDiscoveryCache.TryGetValue(Name, out var discovered)
                ? discovered
                : null;
        }

        // 从缓存获取
        return _typeCache.GetOrAdd(DbContextType, _ => ResolveDbContextType(DbContextType));
    }

    /// <summary>
    /// 自动发现 DbContext 类型（根据 Name 匹配）
    /// </summary>
    /// <param name="logger">日志记录器（可选）</param>
    /// <returns>发现的 DbContext 类型</returns>
    /// <exception cref="InvalidOperationException">当找不到匹配的类型或找到多个匹配时抛出</exception>
    internal Type AutoDiscoverDbContextType(ILogger? logger = null)
    {
        var sw = Stopwatch.StartNew();
        var scannedAssemblyCount = 0;
        
        try
        {
            if (string.IsNullOrEmpty(Name))
            {
                throw new InvalidOperationException(
                    "Cannot auto-discover DbContext type: Name is empty. Please specify DbContextType explicitly.");
            }

            // 检查缓存
            if (_autoDiscoveryCache.TryGetValue(Name, out var cachedType))
            {
                sw.Stop();
                logger?.LogDebug(
                    "Using cached DbContext type '{Type}' for Name '{Name}' (cache hit in {ElapsedMs}ms).",
                    cachedType.FullName, Name, sw.ElapsedMilliseconds);
                return cachedType;
            }

            logger?.LogDebug("Starting DbContext type auto-discovery for Name '{Name}'.", Name);

            // 预加载程序集引用
            var assemblyLoadStart = Stopwatch.StartNew();
            AssemblyScanner.LoadEntryAssemblyReferences(logger);
            assemblyLoadStart.Stop();
            logger?.LogDebug(
                "Loaded entry assembly references in {ElapsedMs}ms for DbContext discovery (Name: {Name}).",
                assemblyLoadStart.ElapsedMilliseconds, Name);

            // 扫描所有 DbContext 类型
            var scanStart = Stopwatch.StartNew();
            var dbContextTypes = new List<Type>();
            var assemblies = AssemblyScanner.GetApplicationAssemblies();

            foreach (var assembly in assemblies)
            {
                try
                {
                    var types = AssemblyScanner.GetTypesSafely(assembly, logger);
                    var dbContextCount = 0;
                    foreach (var type in types)
                    {
                        if (type != null &&
                            typeof(DbContext).IsAssignableFrom(type) &&
                            type.IsClass &&
                            !type.IsAbstract &&
                            !type.IsGenericType)
                        {
                            dbContextTypes.Add(type);
                            dbContextCount++;
                        }
                    }
                    if (dbContextCount > 0)
                    {
                        logger?.LogDebug(
                            "Found {Count} DbContext types in assembly {Assembly} (Name: {Name})",
                            dbContextCount, assembly.GetName().Name, Name);
                    }
                    scannedAssemblyCount++;
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "Failed to scan types from assembly {Assembly} for DbContext discovery (Name: {Name})",
                        assembly.GetName().Name, Name);
                }
            }
            scanStart.Stop();
            logger?.LogDebug(
                "Scanned {AssemblyCount} assemblies and found {DbContextCount} DbContext types in {ElapsedMs}ms (Name: {Name})",
                scannedAssemblyCount, dbContextTypes.Count, scanStart.ElapsedMilliseconds, Name);

        // 按优先级匹配
        var exactMatches = new List<Type>();
        var containsMatches = new List<Type>();
        var nameMatches = new List<Type>();

        var expectedExactName = $"{Name}DbContext";
        var expectedSuffix = "DbContext";

        foreach (var type in dbContextTypes)
        {
            var typeName = type.Name;

            // 优先级1：精确匹配 {Name}DbContext
            if (string.Equals(typeName, expectedExactName, StringComparison.OrdinalIgnoreCase))
            {
                exactMatches.Add(type);
            }
            // 优先级2：包含匹配（类型名包含 {Name} 且以 DbContext 结尾）
            else if (typeName.Contains(Name, StringComparison.OrdinalIgnoreCase) &&
                     typeName.EndsWith(expectedSuffix, StringComparison.OrdinalIgnoreCase))
            {
                containsMatches.Add(type);
            }
            // 优先级3：类型名完全等于 Name
            else if (string.Equals(typeName, Name, StringComparison.OrdinalIgnoreCase))
            {
                nameMatches.Add(type);
            }
        }

        // 按优先级返回结果
        Type? discoveredType = null;
        string matchType = string.Empty;
        
        if (exactMatches.Count > 0)
            {
                if (exactMatches.Count > 1)
                {
                    throw BuildMultipleMatchesException(Name, exactMatches, "exact");
                }
                discoveredType = exactMatches[0];
                matchType = "exact";
            }
            else if (containsMatches.Count > 0)
            {
                if (containsMatches.Count > 1)
                {
                    throw BuildMultipleMatchesException(Name, containsMatches, "contains");
                }
                discoveredType = containsMatches[0];
                matchType = "contains";
            }
            else if (nameMatches.Count > 0)
            {
                if (nameMatches.Count > 1)
                {
                    throw BuildMultipleMatchesException(Name, nameMatches, "name");
                }
                discoveredType = nameMatches[0];
                matchType = "name";
            }
        else
        {
            // 未找到匹配
            throw BuildNoMatchException(Name, assemblies);
        }

        // 缓存结果（discoveredType 不应该为 null，因为我们已经在上面检查过）
        if (discoveredType == null)
        {
            throw new InvalidOperationException($"Discovered DbContext type is null for Name '{Name}'. This should not happen.");
        }

        _autoDiscoveryCache.TryAdd(Name, discoveredType);
        
        sw.Stop();
        logger?.LogInformation(
            "Auto-discovered DbContext type '{Type}' for Name '{Name}' ({MatchType} match) in {ElapsedMs}ms. Scanned {AssemblyCount} assemblies, found {DbContextCount} DbContext types.",
            discoveredType.FullName, Name, matchType, sw.ElapsedMilliseconds, scannedAssemblyCount, dbContextTypes.Count);
        
        return discoveredType;
        }
        catch (InvalidOperationException)
        {
            sw.Stop();
            // 重新抛出异常，但添加性能信息
            logger?.LogWarning(
                "Failed to auto-discover DbContext type for Name '{Name}' after {ElapsedMs}ms. Scanned {AssemblyCount} assemblies.",
                Name, sw.ElapsedMilliseconds, scannedAssemblyCount);
            throw;
        }
    }

    /// <summary>
    /// 构建多个匹配的异常信息
    /// </summary>
    private static InvalidOperationException BuildMultipleMatchesException(string name, List<Type> matches, string matchType)
    {
        var typeList = string.Join("\n", matches.Select((t, i) => $"{i + 1}. {t.FullName}"));
        var suggestedType = matches[0].FullName + ", " + matches[0].Assembly.GetName().Name;

        return new InvalidOperationException(
            $"Multiple DbContext types found for Name '{name}' ({matchType} match). Please explicitly specify DbContextType.\n\n" +
            $"Found types:\n{typeList}\n\n" +
            $"Please specify DbContextType in configuration:\n" +
            $"\"DbContextType\": \"{suggestedType}\"");
    }

    /// <summary>
    /// 构建未找到匹配的异常信息
    /// </summary>
    private static InvalidOperationException BuildNoMatchException(string name, IEnumerable<System.Reflection.Assembly> assemblies)
    {
        var assemblyList = string.Join(", ", assemblies.Select(a => a.GetName().Name).Distinct());
        var expectedName = $"{name}DbContext";

        return new InvalidOperationException(
            $"Cannot auto-discover DbContext type for Name '{name}'. No matching DbContext found in loaded assemblies.\n\n" +
            $"Naming convention: {name} → {expectedName} (e.g., \"Default\" → \"DefaultDbContext\")\n\n" +
            $"Possible solutions:\n" +
            $"1. Explicitly specify DbContextType in configuration:\n" +
            $"   \"DbContextType\": \"MyApp.Data.{expectedName}, MyApp\"\n\n" +
            $"2. Ensure the DbContext assembly is loaded and the type follows the naming convention\n\n" +
            $"Scanned assemblies: {assemblyList}");
    }

    /// <summary>
    /// 解析 DbContext 类型（实际解析逻辑）
    /// </summary>
    private static Type? ResolveDbContextType(string dbContextTypeString)
    {
        // 首先尝试使用 Type.GetType（适用于已加载的程序集）
        var type = Type.GetType(dbContextTypeString);
        if (type != null)
            return type;

        // 如果包含程序集名称，尝试解析程序集名称
        var parts = dbContextTypeString.Split(',');
        if (parts.Length == 2)
        {
            var typeName = parts[0].Trim();
            var assemblyName = parts[1].Trim();

            // 尝试加载程序集
            try
            {
                var assembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == assemblyName || 
                                        a.FullName?.StartsWith(assemblyName + ",") == true);
                
                if (assembly != null)
                {
                    type = assembly.GetType(typeName);
                    if (type != null)
                        return type;
                }
            }
            catch
            {
                // 忽略加载错误，继续尝试其他方法
            }
        }

        // 在所有已加载的程序集中查找类型（按完整名称匹配）
        type = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a =>
            {
                try
                {
                    return a.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    return ex.Types.Where(t => t != null)!;
                }
                catch
                {
                    return Array.Empty<Type>();
                }
            })
            .FirstOrDefault(t => t != null && t.FullName == dbContextTypeString);

        if (type != null)
            return type;

        // 如果完整名称匹配失败，尝试按类型名称匹配（不包含命名空间）
        var simpleTypeName = dbContextTypeString.Contains('.') 
            ? dbContextTypeString[(dbContextTypeString.LastIndexOf('.') + 1)..]
            : dbContextTypeString;

        // 移除程序集名称部分（如果有）
        if (simpleTypeName.Contains(','))
        {
            simpleTypeName = simpleTypeName.Split(',')[0].Trim();
        }

        return AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a =>
            {
                try
                {
                    return a.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    return ex.Types.Where(t => t != null)!;
                }
                catch
                {
                    return Array.Empty<Type>();
                }
            })
            .FirstOrDefault(t => t != null && t.Name == simpleTypeName && typeof(DbContext).IsAssignableFrom(t));
    }
}

