
namespace Tnzi.Extensions;

/// <summary>
/// 对象扩展方法
/// </summary>
public static class ObjectExtensions
{
    #region 类型转换

    /// <summary>
    /// 安全转换为指定类型（使用as操作符）
    /// </summary>
    public static T? As<T>(this object? obj) where T : class
    {
        return obj as T;
    }

    /// <summary>
    /// 转换为指定类型
    /// </summary>
    public static T CastTo<T>(this object? obj)
    {
        if (obj == null)
            return default!;

        if (obj is T result)
            return result;

        try
        {
            return (T)Convert.ChangeType(obj, typeof(T));
        }
        catch
        {
            return default!;
        }
    }

    /// <summary>
    /// 转换为指定类型，失败时返回默认值
    /// </summary>
    /// <remarks>
    /// 不能委托给单参数的 CastTo 重载：该重载自己吞掉异常并返回 default(T)，
    /// 外层 catch 永不触发，defaultValue 会被完全忽略。这里必须自行判定转换失败。
    /// </remarks>
    public static T CastTo<T>(this object? obj, T defaultValue)
    {
        if (obj == null)
            return defaultValue;

        if (obj is T result)
            return result;

        try
        {
            return (T)Convert.ChangeType(obj, typeof(T));
        }
        catch
        {
            return defaultValue;
        }
    }

    /// <summary>
    /// 转换为指定类型
    /// </summary>
    public static object? CastTo(this object? obj, Type conversionType)
    {
        if (obj == null)
            return null;

        if (conversionType.IsNullableType())
        {
            conversionType = conversionType.GetUnderlyingType();
        }

        if (conversionType.IsEnum)
        {
            try
            {
                return Enum.Parse(conversionType, obj.ToString()!);
            }
            catch
            {
                return null; // 解析失败返回 null
            }
        }

        if (conversionType == typeof(Guid))
        {
            try
            {
                return Guid.Parse(obj.ToString()!);
            }
            catch
            {
                return null; // 解析失败返回 null
            }
        }

        return Convert.ChangeType(obj, conversionType);
    }

    /// <summary>
    /// 类型检查
    /// </summary>
    public static bool Is<T>(this object? obj) where T : class
    {
        return obj is T;
    }

    #endregion

    #region 条件操作

    /// <summary>
    /// 如果满足条件，则执行函数
    /// </summary>
    public static T? If<T>(this T obj, Func<T, bool> predicate, Func<T, T> func)
    {
        return predicate(obj) ? func(obj) : obj;
    }

    /// <summary>
    /// 如果不为空，则执行函数
    /// </summary>
    public static T? IfNotNull<T>(this T? obj, Func<T, T> func) where T : class
    {
        return obj != null ? func(obj) : obj;
    }

    /// <summary>
    /// 如果不为空，则执行函数并返回结果
    /// </summary>
    public static TResult? IfNotNull<T, TResult>(this T? obj, Func<T, TResult> func) where T : class
    {
        return obj != null ? func(obj) : default;
    }

    #endregion

    #region 范围检查

    /// <summary>
    /// 判断当前值是否介于指定范围内
    /// </summary>
    /// <typeparam name="T">动态类型</typeparam>
    /// <param name="value">动态类型对象</param>
    /// <param name="start">范围起点</param>
    /// <param name="end">范围终点</param>
    /// <param name="leftEqual">是否可等于下限 <paramref name="start"/>（默认等于）</param>
    /// <param name="rightEqual">是否可等于上限 <paramref name="end"/>（默认等于）</param>
    /// <returns>是否在范围内</returns>
    public static bool IsBetween<T>(this T value, T start, T end, bool leftEqual = true, bool rightEqual = true)
        where T : IComparable<T>
    {
        bool flag = leftEqual ? value.CompareTo(start) >= 0 : value.CompareTo(start) > 0;
        bool flag2 = rightEqual ? value.CompareTo(end) <= 0 : value.CompareTo(end) < 0;
        return flag && flag2;
    }

    /// <summary>
    /// 判断值是否在指定集合中
    /// </summary>
    public static bool IsIn<T>(this T value, params T[] source)
    {
        return source.Contains(value);
    }

    /// <summary>
    /// 判断值是否在指定集合中
    /// </summary>
    public static bool IsIn<T>(this T value, IEnumerable<T> source)
    {
        return source.Contains(value);
    }

    #endregion
}