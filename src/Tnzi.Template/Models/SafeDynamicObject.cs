namespace Tnzi.Template.Models;

/// <summary>
/// 安全的动态对象
/// 当访问不存在的属性时返回 null（而不是抛出异常）
/// 用于 Razor 模板渲染，支持 @Model?.PropertyName 语法
/// </summary>
public class SafeDynamicObject : DynamicObject
{
    private readonly Dictionary<string, object?> _values;

    public SafeDynamicObject()
    {
        _values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 从非可空字典创建
    /// </summary>
    public static SafeDynamicObject FromDictionary(IDictionary<string, object> source)
    {
        var result = new SafeDynamicObject();
        foreach (var kvp in source)
        {
            result._values[kvp.Key] = ConvertValue(kvp.Value);
        }
        return result;
    }

    /// <summary>
    /// 从可空字典创建
    /// </summary>
    public static SafeDynamicObject FromNullableDictionary(IDictionary<string, object?> source)
    {
        var result = new SafeDynamicObject();
        foreach (var kvp in source)
        {
            result._values[kvp.Key] = ConvertValue(kvp.Value);
        }
        return result;
    }

    /// <summary>
    /// 转换值（嵌套字典转换为 SafeDynamicObject）
    /// </summary>
    private static object? ConvertValue(object? value)
    {
        if (value == null) return null;

        // 处理非可空字典
        if (value is IDictionary<string, object> dict)
        {
            return FromDictionary(dict);
        }

        // 处理可空字典
        if (value is IDictionary<string, object?> nullableDict)
        {
            return FromNullableDictionary(nullableDict);
        }

        return value;
    }

    /// <summary>
    /// 尝试获取成员值
    /// 如果属性不存在，返回 null 而不是抛出异常
    /// </summary>
    public override bool TryGetMember(GetMemberBinder binder, out object? result)
    {
        // 尝试获取值，不存在则返回 null
        _values.TryGetValue(binder.Name, out result);
        return true; // 始终返回 true，表示操作成功（即使值为 null）
    }

    /// <summary>
    /// 尝试设置成员值
    /// </summary>
    public override bool TrySetMember(SetMemberBinder binder, object? value)
    {
        _values[binder.Name] = ConvertValue(value);
        return true;
    }

    /// <summary>
    /// 尝试通过索引获取值
    /// 支持 Model["PropertyName"] 语法
    /// </summary>
    public override bool TryGetIndex(GetIndexBinder binder, object[] indexes, out object? result)
    {
        if (indexes.Length == 1 && indexes[0] is string key)
        {
            _values.TryGetValue(key, out result);
            return true;
        }
        result = null;
        return false;
    }

    /// <summary>
    /// 尝试通过索引设置值
    /// 支持 Model["PropertyName"] = value 语法
    /// </summary>
    public override bool TrySetIndex(SetIndexBinder binder, object[] indexes, object? value)
    {
        if (indexes.Length == 1 && indexes[0] is string key)
        {
            _values[key] = ConvertValue(value);
            return true;
        }
        return false;
    }

    /// <summary>
    /// 获取所有动态成员名称
    /// </summary>
    public override IEnumerable<string> GetDynamicMemberNames()
    {
        return _values.Keys;
    }

    /// <summary>
    /// 获取所有成员的键值对（用于复制到新字典）
    /// </summary>
    public IReadOnlyDictionary<string, object?> GetMembers()
    {
        return _values;
    }

    /// <summary>
    /// 检查属性是否存在
    /// </summary>
    public bool HasProperty(string name)
    {
        return _values.ContainsKey(name);
    }

    /// <summary>
    /// 获取属性值，如果不存在返回默认值
    /// </summary>
    public T? GetValueOrDefault<T>(string name, T? defaultValue = default)
    {
        if (_values.TryGetValue(name, out var value) && value is T typedValue)
        {
            return typedValue;
        }
        return defaultValue;
    }

    /// <summary>
    /// 转换为字符串（调试用）
    /// </summary>
    public override string ToString()
    {
        return $"SafeDynamicObject({_values.Count} properties)";
    }
}
