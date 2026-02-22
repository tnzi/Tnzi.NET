namespace Tnzi.Extensions;

/// <summary>
/// 字典辅助扩展方法
/// </summary>
public static class DictionaryExtensions
{
    /// <summary>
    /// 从字典中获取值, 不存在则返回字典<typeparamref name="TValue"/>类型的默认值
    /// </summary>
    /// <typeparam name="TKey">字典键类型</typeparam>
    /// <typeparam name="TValue">字典值类型</typeparam>
    /// <param name="dictionary">要操作的字典</param>
    /// <param name="key">指定键名</param>
    /// <returns>获取到的值</returns>
    public static TValue? GetOrDefault<TKey, TValue>(this IDictionary<TKey, TValue> dictionary, TKey key)
    {
        return dictionary.TryGetValue(key, out TValue? value) ? value : default;
    }

    /// <summary>
    /// 获取指定键的值, 不存在则按指定委托添加值
    /// </summary>
    /// <typeparam name="TKey">字典键类型</typeparam>
    /// <typeparam name="TValue">字典值类型</typeparam>
    /// <param name="dictionary">要操作的字典</param>
    /// <param name="key">指定键名</param>
    /// <param name="addFunc">添加值的委托</param>
    /// <returns>获取到的值</returns>
    public static TValue GetOrAdd<TKey, TValue>(this IDictionary<TKey, TValue> dictionary, TKey key, Func<TValue> addFunc)
    {
        if (dictionary.TryGetValue(key, out TValue? value))
        {
            return value;
        }
        return dictionary[key] = addFunc();
    }

    /// <summary>
    /// 获取指定键指定类型的值
    /// </summary>
    public static T? GetValue<T>(this IDictionary<string, object> dictionary, string key)
    {
        if (dictionary.TryGetValue(key, out var value))
        {
            return value.CastTo<T>();
        }
        return default;
    }

    /// <summary>
    /// 设置字典中的值，不存在则添加，存在则更新
    /// </summary>
    /// <typeparam name="T">值类型</typeparam>
    /// <param name="dictionary">字典</param>
    /// <param name="key">键</param>
    /// <param name="addFunc">添加值的委托</param>
    /// <param name="updateFunc">更新值的委托</param>
    public static void SetValue<T>(this IDictionary<string, object> dictionary, string key, Func<T> addFunc, Func<T, T> updateFunc)
    {
        object? value;
        value = dictionary.TryGetValue(key, out value)
            ? updateFunc(value.CastTo<T>())
            : addFunc();
        dictionary[key] = value!;
    }
}
