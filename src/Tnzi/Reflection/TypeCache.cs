
namespace Tnzi.Reflection;

/// <summary>
/// 类型元数据缓存
/// 用于缓存反射结果，减少运行时反射开销
/// </summary>
public class TypeMetadata
{
    public Type Type { get; }
    public PropertyInfo[] Properties { get; }
    public MethodInfo[] Methods { get; }
    public Attribute[] Attributes { get; }
    public Type[] Interfaces { get; }
    public ConstructorInfo[] Constructors { get; }

    public TypeMetadata(Type type)
    {
        Type = type;
        Properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        Methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance);
        Attributes = type.GetCustomAttributes().ToArray();
        Interfaces = type.GetInterfaces();
        Constructors = type.GetConstructors();
    }

    /// <summary>
    /// 获取指定名称的属性
    /// </summary>
    public PropertyInfo? GetProperty(string name)
        => Properties.FirstOrDefault(p => p.Name == name);

    /// <summary>
    /// 获取指定名称的方法
    /// </summary>
    public MethodInfo? GetMethod(string name)
        => Methods.FirstOrDefault(m => m.Name == name);

    /// <summary>
    /// 检查是否有指定类型的特性
    /// </summary>
    public bool HasAttribute<TAttribute>() where TAttribute : Attribute
        => Attributes.Any(a => a is TAttribute);

    /// <summary>
    /// 获取指定类型的特性
    /// </summary>
    public TAttribute? GetAttribute<TAttribute>() where TAttribute : Attribute
        => Attributes.OfType<TAttribute>().FirstOrDefault();

    /// <summary>
    /// 检查是否实现了指定接口
    /// </summary>
    public bool ImplementsInterface<TInterface>()
        => Interfaces.Contains(typeof(TInterface));

    /// <summary>
    /// 检查是否实现了指定泛型接口
    /// </summary>
    public bool ImplementsGenericInterface(Type genericInterfaceDefinition)
        => Interfaces.Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == genericInterfaceDefinition);
}

/// <summary>
/// 类型缓存
/// 提供线程安全的类型元数据缓存
/// </summary>
public static class TypeCache
{
    private static readonly ConcurrentDictionary<Type, TypeMetadata> _cache = new();

    /// <summary>
    /// 获取或添加类型元数据
    /// </summary>
    public static TypeMetadata GetOrAdd(Type type)
        => _cache.GetOrAdd(type, t => new TypeMetadata(t));

    /// <summary>
    /// 获取或添加类型元数据（泛型版本）
    /// </summary>
    public static TypeMetadata GetOrAdd<T>()
        => GetOrAdd(typeof(T));

    /// <summary>
    /// 清除指定类型的缓存
    /// </summary>
    public static bool Remove(Type type)
        => _cache.TryRemove(type, out _);

    /// <summary>
    /// 清除所有缓存
    /// </summary>
    public static void Clear()
        => _cache.Clear();

    /// <summary>
    /// 获取缓存的类型数量
    /// </summary>
    public static int Count => _cache.Count;
}

/// <summary>
/// 程序集类型扫描器
/// 提供缓存的程序集类型扫描
/// </summary>
public static class AssemblyTypeScanner
{
    private static readonly ConcurrentDictionary<string, List<Type>> _scanCache = new();

    /// <summary>
    /// 扫描实现指定接口的类型
    /// </summary>
    public static IReadOnlyList<Type> ScanTypes<TInterface>(params Assembly[] assemblies)
        => ScanTypes(typeof(TInterface), assemblies);

    /// <summary>
    /// 扫描实现指定接口的类型
    /// </summary>
    public static IReadOnlyList<Type> ScanTypes(Type interfaceType, params Assembly[] assemblies)
    {
        var targetAssemblies = assemblies.Length > 0 
            ? assemblies 
            : AppDomain.CurrentDomain.GetAssemblies();

        var cacheKey = $"{interfaceType.FullName}:{string.Join(",", targetAssemblies.Select(a => a.GetName().Name))}";

        return _scanCache.GetOrAdd(cacheKey, _ =>
        {
            var types = new List<Type>();
            
            foreach (var assembly in targetAssemblies)
            {
                try
                {
                    // 跳过系统程序集
                    var name = assembly.GetName().Name ?? "";
                    if (name.StartsWith("System") || name.StartsWith("Microsoft") || name.StartsWith("netstandard"))
                        continue;

                    var matchingTypes = assembly.GetExportedTypes()
                        .Where(t => t.IsClass && !t.IsAbstract && interfaceType.IsAssignableFrom(t));
                    
                    types.AddRange(matchingTypes);
                }
                catch
                {
                    // 忽略无法加载类型的程序集
                }
            }

            return types;
        });
    }

    /// <summary>
    /// 清除扫描缓存
    /// </summary>
    public static void ClearCache()
        => _scanCache.Clear();
}