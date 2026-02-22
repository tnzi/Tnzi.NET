
namespace Tnzi.EFCore.Internal;

/// <summary>
/// 类型辅助工具类
/// 提供统一的类型检查、解析和元数据处理功能
/// </summary>
public static class TypeHelper
{
    private static readonly ConcurrentDictionary<Type, bool> EntityTypeCache = new();

    /// <summary>
    /// 检查类型是否为有效的实体类型（实现了 IEntity 接口且不是抽象类）
    /// </summary>
    /// <param name="type">要检查的类型</param>
    /// <returns>如果是实体类型返回 true</returns>
    public static bool IsEntityType(Type type)
    {
        if (type == null) return false;

        return EntityTypeCache.GetOrAdd(type, t =>
        {
            return typeof(IEntity).IsAssignableFrom(t) 
                && t.IsClass 
                && !t.IsAbstract 
                && !t.IsInterface;
        });
    }

    /// <summary>
    /// 获取实体的受限主键类型 TKey
    /// </summary>
    /// <param name="entityType">实体类型</param>
    /// <returns>主键类型，如果未找到则返回 null</returns>
    public static Type? GetPrimaryKeyType(Type entityType)
    {
        var interfaceType = entityType.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEntity<>));
        
        return interfaceType?.GetGenericArguments()[0];
    }

    /// <summary>
    /// 判断是否为泛型类型
    /// </summary>
    public static bool IsGenericType(Type type, Type genericTypeDefinition)
    {
        return type.IsGenericType && type.GetGenericTypeDefinition() == genericTypeDefinition;
    }

    /// <summary>
    /// 获取接口的第一个泛型参数
    /// </summary>
    public static Type? GetFirstGenericArgument(Type type, Type interfaceTypeDefinition)
    {
        var interfaceType = type.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == interfaceTypeDefinition);
        
        return interfaceType?.GetGenericArguments().FirstOrDefault();
    }
    private static readonly ConcurrentDictionary<Type, bool> FileFieldsCache = new();

    /// <summary>
    /// 检查实体类型是否包含标记了 [FileField] 的属性
    /// </summary>
    public static bool HasFileFields(Type type)
    {
        if (type == null) return false;

        return FileFieldsCache.GetOrAdd(type, t =>
        {
            return t.GetProperties().Any(p => p.GetCustomAttribute<FileFieldAttribute>() != null);
        });
    }
}