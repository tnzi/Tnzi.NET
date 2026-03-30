
namespace Tnzi.Utilities;

/// <summary>
/// 集合类型字符串排序操作类
/// </summary>
/// <typeparam name="T">集合项类型</typeparam>
public static class CollectionPropertySorter<T>
{
    // ReSharper disable StaticFieldInGenericType
    private static readonly ConcurrentDictionary<string, LambdaExpression> Cache = new ConcurrentDictionary<string, LambdaExpression>();

    /// <summary>
    /// 按指定的属性名称对<see cref="IQueryable{T}"/>序列进行排序
    /// </summary>
    /// <param name="source">IQueryable{T}序列</param>
    /// <param name="propertyName">属性名称</param>
    /// <param name="sortDirection">排序方向</param>
    /// <returns></returns>
    public static IOrderedQueryable<T> OrderBy(IQueryable<T> source, string propertyName, ListSortDirection sortDirection)
    {
        Check.NotNullOrEmpty(propertyName);

        dynamic keySelector = GetKeySelector(propertyName);
        return sortDirection == ListSortDirection.Ascending
            ? System.Linq.Queryable.OrderBy(source, keySelector)
            : System.Linq.Queryable.OrderByDescending(source, keySelector);
    }

    /// <summary>
    /// 按指定的属性名称对<see cref="IOrderedQueryable{T}"/>序列进行排序
    /// </summary>
    /// <param name="source">IOrderedQueryable{T}序列</param>
    /// <param name="propertyName">属性名称</param>
    /// <param name="sortDirection">排序方向</param>
    /// <returns></returns>
    public static IOrderedQueryable<T> ThenBy(IOrderedQueryable<T> source, string propertyName, ListSortDirection sortDirection)
    {
        Check.NotNullOrEmpty(propertyName);

        dynamic keySelector = GetKeySelector(propertyName);
        return sortDirection == ListSortDirection.Ascending
            ? System.Linq.Queryable.ThenBy(source, keySelector)
            : System.Linq.Queryable.ThenByDescending(source, keySelector);
    }

    /// <summary>
    /// 按指定的属性名称对<see cref="IEnumerable{T}"/>序列进行排序
    /// </summary>
    /// <param name="source"><see cref="IEnumerable{T}"/>序列</param>
    /// <param name="propertyName">属性名称</param>
    /// <param name="sortDirection">排序方向</param>
    public static IOrderedEnumerable<T> OrderBy(IEnumerable<T> source, string propertyName, ListSortDirection sortDirection)
    {
        Check.NotNullOrEmpty(propertyName);

        dynamic expression = GetKeySelector(propertyName);
        dynamic keySelector = expression.Compile();
        return sortDirection == ListSortDirection.Ascending
            ? Enumerable.OrderBy(source, keySelector)
            : Enumerable.OrderByDescending(source, keySelector);
    }

    /// <summary>
    /// 按指定的属性名称对<see cref="IOrderedEnumerable{T}"/>进行继续排序
    /// </summary>
    /// <param name="source"><see cref="IOrderedEnumerable{T}"/>序列</param>
    /// <param name="propertyName">属性名称</param>
    /// <param name="sortDirection">排序方向</param>
    public static IOrderedEnumerable<T> ThenBy(IOrderedEnumerable<T> source, string propertyName, ListSortDirection sortDirection)
    {
        Check.NotNullOrEmpty(propertyName);

        dynamic expression = GetKeySelector(propertyName);
        dynamic keySelector = expression.Compile();
        return sortDirection == ListSortDirection.Ascending
            ? System.Linq.Enumerable.ThenBy(source, keySelector)
            : System.Linq.Enumerable.ThenByDescending(source, keySelector);
    }

    private static LambdaExpression GetKeySelector(string keyName)
    {
        Type type = typeof(T);
        string key = type.FullName + "." + keyName;
        if (Cache.TryGetValue(key, out var cached))
        {
            return cached;
        }
        
        ParameterExpression param = Expression.Parameter(type);
        string[] propertyNames = keyName.Split('.');
        Expression propertyAccess = param;
        foreach (string propertyName in propertyNames)
        {
            PropertyInfo? property = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (property == null)
            {
                throw new InvalidOperationException($"Property '{propertyName}' not found on type '{type.Name}'.");
            }
            type = property.PropertyType;
            propertyAccess = Expression.MakeMemberAccess(propertyAccess, property);
        }
        LambdaExpression keySelector = Expression.Lambda(propertyAccess, param);
        Cache[key] = keySelector;
        return keySelector;
    }
}