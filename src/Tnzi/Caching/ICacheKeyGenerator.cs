namespace Tnzi.Caching;

/// <summary>
/// 缓存键生成器接口
/// </summary>
public interface ICacheKeyGenerator
{
    /// <summary>
    /// 生成缓存键
    /// </summary>
    /// <param name="prefix">键前缀</param>
    /// <param name="parameters">参数数组</param>
    /// <returns>生成的缓存键</returns>
    string Generate(string prefix, params object[] parameters);

    /// <summary>
    /// 生成缓存键（带分隔符）
    /// </summary>
    /// <param name="separator">分隔符</param>
    /// <param name="parts">键的各个部分</param>
    /// <returns>生成的缓存键</returns>
    string GenerateWithSeparator(string separator, params string[] parts);

    /// <summary>
    /// 生成缓存键（基于类型和方法）
    /// </summary>
    /// <param name="typeName">类型名称</param>
    /// <param name="methodName">方法名称</param>
    /// <param name="parameters">参数数组</param>
    /// <returns>生成的缓存键</returns>
    string GenerateForMethod(string typeName, string methodName, params object[] parameters);

    /// <summary>
    /// 生成查询缓存键（基于查询表达式）
    /// </summary>
    /// <typeparam name="T">实体类型</typeparam>
    /// <param name="expressionString">查询表达式字符串</param>
    /// <param name="additionalParams">额外参数</param>
    /// <returns>生成的缓存键</returns>
    string GenerateForQuery<T>(string expressionString, params object[] additionalParams);
}

