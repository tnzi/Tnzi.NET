
namespace Tnzi.Extensions;

/// <summary>
/// JSON扩展方法
/// </summary>
public static class JsonExtensions
{
    private static readonly JsonSerializerOptions DefaultOptions = new()
    {
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = null,
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping // 处理Unicode编码
    };

    private static readonly JsonSerializerOptions CamelCaseOptions = new(DefaultOptions)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly JsonSerializerOptions IndentedOptions = new(DefaultOptions)
    {
        WriteIndented = true
    };

    private static readonly JsonSerializerOptions CamelCaseIndentedOptions = new(CamelCaseOptions)
    {
        WriteIndented = true
    };

    /// <summary>
    /// 将对象序列化为JSON字符串
    /// </summary>
    /// <param name="obj">要序列化的对象</param>
    /// <param name="camelCase">是否使用驼峰命名</param>
    /// <param name="indented">是否格式化（缩进）</param>
    /// <returns>JSON字符串</returns>
    public static string ToJsonString(this object? obj, bool camelCase = false, bool indented = false)
    {
        if (obj == null)
            return string.Empty;

        try
        {
            var options = GetOptions(camelCase, indented);
            return JsonSerializer.Serialize(obj, options);
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// 将对象序列化为JSON字符串（默认格式）
    /// </summary>
    public static string ToJsonString(this object? obj)
    {
        return obj.ToJsonString(false, false);
    }

    /// <summary>
    /// 将JSON字符串反序列化为对象
    /// </summary>
    /// <typeparam name="T">目标类型</typeparam>
    /// <param name="json">JSON字符串</param>
    /// <param name="camelCase">是否使用驼峰命名</param>
    /// <returns>反序列化的对象</returns>
    public static T? FromJsonString<T>(this string? json, bool camelCase = false)
    {
        if (string.IsNullOrWhiteSpace(json))
            return default;

        try
        {
            var options = GetOptions(camelCase, false);
            return JsonSerializer.Deserialize<T>(json, options);
        }
        catch
        {
            return default;
        }
    }

    /// <summary>
    /// 将JSON字符串反序列化为对象（默认格式）
    /// </summary>
    public static T? FromJsonString<T>(this string? json)
    {
        return json.FromJsonString<T>(false);
    }

    /// <summary>
    /// 将JSON字符串反序列化为对象
    /// </summary>
    /// <param name="json">JSON字符串</param>
    /// <param name="camelCase">是否使用驼峰命名</param>
    /// <returns>反序列化的对象</returns>
    public static object? FromJsonString(this string? json, bool camelCase = false)
    {
        return json.FromJsonString<object>(camelCase);
    }

    /// <summary>
    /// 获取JSON序列化选项
    /// </summary>
    private static JsonSerializerOptions GetOptions(bool camelCase, bool indented)
    {
        return (camelCase, indented) switch
        {
            (false, false) => DefaultOptions,
            (true, false) => CamelCaseOptions,
            (false, true) => IndentedOptions,
            (true, true) => CamelCaseIndentedOptions
        };
    }
}