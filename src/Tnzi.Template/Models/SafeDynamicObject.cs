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
    /// 转换值（JSON 元素归一为 CLR 原生类型，嵌套字典转换为 SafeDynamicObject）
    /// </summary>
    private static object? ConvertValue(object? value)
    {
        if (value == null) return null;

        // 模板变量经 HTTP 传入时（Dictionary<string, object> 的 JSON 反序列化结果）每个值
        // 都是 JsonElement。它不是 string/bool/int，模板里任何类型化运算都会在运行期抛异常
        // （@Model.Flag == true 抛 "Operator '==' cannot be applied to operands of type
        // 'JsonElement' and 'bool'"，string.IsNullOrEmpty(@Model.Url) 抛无匹配重载），
        // 只有纯输出（走 ToString）才侥幸可用。这里先归一成原生类型，让「进程内直接传
        // C# 值」与「经 API 传 JSON」两条路径对模板等价。
        if (value is JsonElement element)
        {
            return ConvertJsonElement(element);
        }

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
    /// 将 JsonElement 归一为 CLR 原生类型（对象递归为 SafeDynamicObject，数组递归为 List）
    /// </summary>
    private static object? ConvertJsonElement(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return null;
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            case JsonValueKind.String:
                return element.GetString();
            case JsonValueKind.Number:
                return ConvertJsonNumber(element);
            case JsonValueKind.Object:
                var members = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var property in element.EnumerateObject())
                {
                    members[property.Name] = ConvertJsonElement(property.Value);
                }
                return FromNullableDictionary(members);
            case JsonValueKind.Array:
                var items = new List<object?>();
                foreach (var item in element.EnumerateArray())
                {
                    items.Add(ConvertJsonElement(item));
                }
                return items;
            default:
                return element.ToString();
        }
    }

    /// <summary>
    /// 归一 JSON 数值：按 int → long → decimal → double 逐级放宽，保留整数的整数形态
    /// （模板里的 @Model.Count 等值比较依赖它不是 double）
    /// </summary>
    private static object ConvertJsonNumber(JsonElement element)
    {
        if (element.TryGetInt32(out var intValue)) return intValue;
        if (element.TryGetInt64(out var longValue)) return longValue;
        if (element.TryGetDecimal(out var decimalValue)) return decimalValue;
        return element.GetDouble();
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
