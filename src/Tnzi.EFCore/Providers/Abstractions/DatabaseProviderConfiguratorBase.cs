
namespace Tnzi.EFCore.Providers;

/// <summary>
/// 数据库提供者配置器基类
/// 提供反射查找和调用扩展方法的公共逻辑
/// </summary>
public abstract class DatabaseProviderConfiguratorBase : IDatabaseProviderConfigurator
{
    private static readonly ConcurrentDictionary<string, Assembly?> _assemblyCache = new();
    private static readonly ConcurrentDictionary<(Assembly, string), MethodInfo?> _methodCache = new();

    /// <summary>
    /// 支持的提供者类型
    /// </summary>
    public abstract DatabaseProvider Provider { get; }
    
    /// <summary>
    /// 提供者程序集名称
    /// </summary>
    protected abstract string AssemblyName { get; }
    
    /// <summary>
    /// 扩展方法名称（如 UseSqlServer、UseNpgsql 等）
    /// </summary>
    protected abstract string ExtensionMethodName { get; }
    
    /// <summary>
    /// 配置 DbContextOptionsBuilder
    /// </summary>
    public virtual void Configure(DbContextOptionsBuilder builder, string connectionString, DbProviderConfigureOptions? options = null)
    {
        var assembly = FindAssembly(AssemblyName);
        if (assembly == null)
        {
            throw new InvalidOperationException(
                $"{AssemblyName} package is not installed. " +
                "Please ensure the package is referenced in your project.");
        }

        var method = FindExtensionMethod(assembly, ExtensionMethodName);
        if (method == null)
        {
            throw new InvalidOperationException(
                $"{ExtensionMethodName} extension method not found in {AssemblyName}. " +
                "Please ensure the package version is compatible.");
        }

        InvokeExtensionMethod(method, builder, connectionString, options);
    }
    
    /// <summary>
    /// 查找程序集
    /// </summary>
    protected static Assembly? FindAssembly(string assemblyName)
    {
        return _assemblyCache.GetOrAdd(assemblyName, name =>
        {
            // 先从已加载的程序集中查找（精确匹配）
            var assembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == name);

            if (assembly != null)
                return assembly;

            // 尝试动态加载程序集
            try
            {
                return Assembly.Load(new AssemblyName(name));
            }
            catch (Exception)
            {
                // 加载失败，尝试通过 FullName 前缀匹配（处理版本差异）
                var matchingAssembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a =>
                        a.FullName?.StartsWith(name + ",", StringComparison.OrdinalIgnoreCase) == true);
                return matchingAssembly;
            }
        });
    }
    
    /// <summary>
    /// 查找扩展方法
    /// </summary>
    protected virtual MethodInfo? FindExtensionMethod(Assembly assembly, string methodName)
    {
        return _methodCache.GetOrAdd((assembly, methodName), key =>
        {
            var (asm, name) = key;
            // 确保程序集的所有类型都被加载
            Type[]? allTypes = null;
            try
            {
                allTypes = asm.GetTypes();
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
            foreach (var type in allTypes)
            {
                try
                {
                    if (type == null || !type.IsClass || !type.IsSealed || type.IsGenericType)
                        continue;

                    var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                        .Where(m => m.Name == name)
                        .ToList();
                    
                    // 优先查找 2 个参数的重载 (builder, connectionString)
                    var extensionMethod = methods.FirstOrDefault(m =>
                    {
                        var parameters = m.GetParameters();
                        if (parameters.Length != 2)
                            return false;
                        
                        var firstParam = parameters[0].ParameterType;
                        var secondParam = parameters[1].ParameterType;
                        
                        return firstParam == typeof(DbContextOptionsBuilder) && secondParam == typeof(string);
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
                        return extensionMethod;
                }
                catch (Exception)
                {
                    // 忽略单个类型的错误，继续查找（某些类型可能无法加载）
                }
            }

            return null;
        });
    }
    
    /// <summary>
    /// 调用扩展方法
    /// </summary>
    protected virtual void InvokeExtensionMethod(MethodInfo method, DbContextOptionsBuilder builder, string connectionString, DbProviderConfigureOptions? options)
    {
        var parameters = method.GetParameters();
        var firstParamType = parameters[0].ParameterType;

        if (firstParamType != typeof(DbContextOptionsBuilder))
        {
            throw new InvalidOperationException(
                $"{method.Name} method first parameter type '{firstParamType.Name}' is not supported. " +
                $"Expected DbContextOptionsBuilder.");
        }

        if (parameters.Length == 2)
        {
            // 标准调用：builder, connectionString —— 该重载无 options action 槽位，
            // provider 级选项（重试/超时）无处应用（真实 provider 均提供带 action 的重载，故极少走到）
            method.Invoke(null, new object[] { builder, connectionString });
        }
        else if (parameters.Length == 3)
        {
            // 调用：builder, connectionString, action —— 第三参为 Action<{ProviderOptionsBuilder}>
            // 把重试/超时选项与 provider 特定的额外配置（如 pgvector 的 UseVector）合成到该 action
            var optionsBuilderType = parameters[2].ParameterType.GetGenericArguments()[0];
            var extraAction = BuildExtraOptionsAction(optionsBuilderType);
            var action = BuildProviderOptionsAction(optionsBuilderType, options, extraAction);
            method.Invoke(null, new object?[] { builder, connectionString, action });
        }
        else
        {
            throw new InvalidOperationException(
                $"{method.Name} method has {parameters.Length} parameters, which is not supported.");
        }
    }

    /// <summary>
    /// provider 特定的额外 options 配置（默认无）。
    /// PostgreSQL 覆盖此方法以在 pgvector 可用时启用 <c>UseVector()</c>。
    /// </summary>
    /// <param name="optionsBuilderType">provider 的 options builder 类型（如 NpgsqlDbContextOptionsBuilder）</param>
    /// <returns>类型为 <c>Action&lt;{optionsBuilderType}&gt;</c> 的委托，或 null 表示无额外配置</returns>
    protected virtual Delegate? BuildExtraOptionsAction(Type optionsBuilderType) => null;

    /// <summary>
    /// 构建应用到 provider options builder 的 <c>Action&lt;{optionsBuilderType}&gt;</c> 委托，
    /// 依次应用重试/超时选项与额外配置（如 pgvector）。无任何设置时返回 null（保持传 null，与升级前行为一致）。
    /// </summary>
    protected static object? BuildProviderOptionsAction(Type optionsBuilderType, DbProviderConfigureOptions? options, Delegate? extraAction)
    {
        var opts = options ?? DbProviderConfigureOptions.None;
        if (!opts.HasAny && extraAction == null)
        {
            return null;
        }

        var typed = typeof(DatabaseProviderConfiguratorBase)
            .GetMethod(nameof(BuildTypedOptionsAction), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(optionsBuilderType);
        return typed.Invoke(null, new object?[] { opts, extraAction });
    }

    /// <summary>
    /// 泛型构建强类型 <c>Action&lt;TBuilder&gt;</c>，闭包内先应用重试/超时，再链式调用额外配置。
    /// </summary>
    private static Action<TBuilder> BuildTypedOptionsAction<TBuilder>(DbProviderConfigureOptions options, Delegate? extraAction)
    {
        var extra = extraAction as Action<TBuilder>;
        return builder =>
        {
            ApplyRetryAndTimeout(typeof(TBuilder), builder!, options);
            extra?.Invoke(builder);
        };
    }

    /// <summary>
    /// 通过反射把 EnableRetryOnFailure / CommandTimeout 应用到 provider 的 options builder。
    /// provider 缺少对应方法时（如 SQLite 无 EnableRetryOnFailure）静默跳过。
    /// </summary>
    private static void ApplyRetryAndTimeout(Type builderType, object builder, DbProviderConfigureOptions options)
    {
        if (options.EnableRetryOnFailure)
        {
            MethodInfo? retryMethod = options.MaxRetryCount.HasValue
                ? builderType.GetMethod("EnableRetryOnFailure", new[] { typeof(int) })
                : builderType.GetMethod("EnableRetryOnFailure", Type.EmptyTypes);
            // 若首选重载不存在，回退到另一重载
            retryMethod ??= builderType.GetMethod("EnableRetryOnFailure", Type.EmptyTypes)
                ?? builderType.GetMethod("EnableRetryOnFailure", new[] { typeof(int) });

            if (retryMethod != null)
            {
                var args = retryMethod.GetParameters().Length == 1
                    ? new object[] { options.MaxRetryCount ?? 6 }
                    : Array.Empty<object>();
                retryMethod.Invoke(builder, args);
            }
        }

        if (options.CommandTimeout.HasValue)
        {
            var timeoutMethod = builderType.GetMethod("CommandTimeout", new[] { typeof(int?) })
                ?? builderType.GetMethod("CommandTimeout", new[] { typeof(int) });
            if (timeoutMethod != null)
            {
                // 装箱的 int 对 int? 与 int 两种形参均可（反射 Invoke 自动处理 Nullable 转换）
                timeoutMethod.Invoke(builder, new object?[] { options.CommandTimeout.Value });
            }
        }
    }
}