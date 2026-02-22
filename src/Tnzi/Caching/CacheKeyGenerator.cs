
namespace Tnzi.Caching;

/// <summary>
/// 缓存键生成器实现
/// </summary>
public class CacheKeyGenerator : ICacheKeyGenerator
{
    private const string DefaultSeparator = ":";

    /// <summary>
    /// 生成缓存键
    /// </summary>
    /// <param name="prefix">键前缀</param>
    /// <param name="parameters">参数数组</param>
    /// <returns>生成的缓存键</returns>
    public string Generate(string prefix, params object[] parameters)
    {
        Check.NotNullOrEmpty(prefix);

        if (parameters == null || parameters.Length == 0)
            return prefix;

        var parts = new List<string> { prefix };
        foreach (var param in parameters)
        {
            if (param == null)
            {
                parts.Add("null");
            }
            else if (param is string str)
            {
                parts.Add(str);
            }
            else if (param is ValueType)
            {
                parts.Add(param.ToString() ?? "null");
            }
            else
            {
                // 对于复杂对象，使用JSON序列化后计算哈希值
                var json = JsonSerializer.Serialize(param);
                var hash = ComputeHash(json);
                parts.Add(hash);
            }
        }

        return string.Join(DefaultSeparator, parts);
    }

    /// <summary>
    /// 生成缓存键（带分隔符）
    /// </summary>
    /// <param name="separator">分隔符</param>
    /// <param name="parts">键的各个部分</param>
    /// <returns>生成的缓存键</returns>
    public string GenerateWithSeparator(string separator, params string[] parts)
    {
        Check.NotNullOrEmpty(parts);

        separator = separator ?? DefaultSeparator;
        return string.Join(separator, parts);
    }

    /// <summary>
    /// 生成缓存键（基于类型和方法）
    /// </summary>
    /// <param name="typeName">类型名称</param>
    /// <param name="methodName">方法名称</param>
    /// <param name="parameters">参数数组</param>
    /// <returns>生成的缓存键</returns>
    public string GenerateForMethod(string typeName, string methodName, params object[] parameters)
    {
        Check.NotNullOrEmpty(typeName);
        Check.NotNullOrEmpty(methodName);

        var prefix = $"{typeName}{DefaultSeparator}{methodName}";
        return Generate(prefix, parameters);
    }

    /// <summary>
    /// 生成查询缓存键（基于查询表达式）
    /// </summary>
    /// <typeparam name="T">实体类型</typeparam>
    /// <param name="expressionString">查询表达式字符串</param>
    /// <param name="additionalParams">额外参数</param>
    /// <returns>生成的缓存键</returns>
    public string GenerateForQuery<T>(string expressionString, params object[] additionalParams)
    {
        var typeName = typeof(T).FullName ?? typeof(T).Name;
        var combined = $"{typeName}:{expressionString}";
        
        if (additionalParams != null && additionalParams.Length > 0)
        {
            var paramsString = string.Join("|", additionalParams.Select(p => p?.ToString() ?? "null"));
            combined = $"{combined}:{paramsString}";
        }

        // 使用哈希值生成短键
        var hash = ComputeHash(combined);
        return $"cache:query:{typeName}:{hash}";
    }

    /// <summary>
    /// 计算字符串的哈希值（用于复杂对象的键生成）
    /// </summary>
    /// <param name="input">输入字符串</param>
    /// <returns>哈希值（十六进制字符串）</returns>
    private static string ComputeHash(string input)
    {
        if (string.IsNullOrEmpty(input))
            return "empty";

        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(input);
        var hashBytes = sha256.ComputeHash(bytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant()[..16]; // 取前16个字符
    }
}