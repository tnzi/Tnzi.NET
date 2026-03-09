
namespace Tnzi.Utilities;

/// <summary>
/// 连接字符串展开器
/// 支持环境变量和配置值的占位符替换（适用于任何模块的连接字符串字段）
/// </summary>
public static class ConnectionStringExpander
{
    // 缓存编译后的正则表达式
    // 超时设置为 1 秒，确保对于复杂连接字符串也能正常处理
    private static readonly Regex PlaceholderRegex = new(
        @"\$\{([^}]+)\}|\$\(([^)]+)\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        TimeSpan.FromSeconds(1));

    /// <summary>
    /// 展开连接字符串中的占位符
    /// </summary>
    /// <param name="connectionString">原始连接字符串</param>
    /// <param name="configuration">配置对象（可选，用于从 IConfiguration 读取值）</param>
    /// <param name="logger">日志记录器（可选）</param>
    /// <returns>展开后的连接字符串</returns>
    public static string Expand(string connectionString, IConfiguration? configuration = null, ILogger? logger = null)
    {
        if (string.IsNullOrEmpty(connectionString))
            return connectionString;

        return PlaceholderRegex.Replace(connectionString, match =>
        {
            // 提取变量名（支持 ${VAR} 和 $(VAR) 两种格式）
            var variableName = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
            var placeholder = match.Value;

            // 尝试从环境变量读取
            var value = Environment.GetEnvironmentVariable(variableName);
            if (!string.IsNullOrEmpty(value))
            {
                logger?.LogDebug("Expanded placeholder {Placeholder} from environment variable {Variable}",
                    placeholder, variableName);
                return value;
            }

            // 尝试从 IConfiguration 读取
            if (configuration != null)
            {
                // 首先尝试直接使用变量名读取
                value = configuration[variableName];
                if (!string.IsNullOrEmpty(value))
                {
                    logger?.LogDebug("Expanded placeholder {Placeholder} from configuration {Variable}",
                        placeholder, variableName);
                    return value;
                }

                // 尝试按 ASP.NET Core 的双下划线分隔格式读取（仅当变量名包含双下划线时）
                // 注意：这是为了支持 ASP.NET Core 环境变量格式（如 Database__ConnectionString__Default）
                // 将双下划线替换为冒号以匹配配置键格式
                if (variableName.Contains("__", StringComparison.Ordinal))
                {
                    var configKey = variableName.Replace("__", ":");
                    value = configuration[configKey];
                    if (!string.IsNullOrEmpty(value))
                    {
                        logger?.LogDebug("Expanded placeholder {Placeholder} from configuration key {ConfigKey} (converted from {Variable})",
                            placeholder, configKey, variableName);
                        return value;
                    }
                }
            }

            // 占位符未解析，保留原占位符并记录警告
            logger?.LogWarning(
                "Placeholder {Placeholder} (variable: {Variable}) could not be resolved. " +
                "Please ensure the environment variable or configuration value is set. " +
                "The placeholder will be kept as-is in the connection string.",
                placeholder, variableName);
            return placeholder;
        });
    }

    /// <summary>
    /// 检查连接字符串是否包含占位符
    /// </summary>
    /// <param name="connectionString">连接字符串</param>
    /// <returns>如果包含占位符返回 true</returns>
    public static bool ContainsPlaceholders(string connectionString)
    {
        if (string.IsNullOrEmpty(connectionString))
            return false;

        return PlaceholderRegex.IsMatch(connectionString);
    }
}
