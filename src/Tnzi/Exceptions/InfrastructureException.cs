namespace Tnzi.Exceptions;

/// <summary>
/// 基础设施异常基类
/// 用于表示基础设施层（数据库、缓存、消息队列、配置等）的错误
/// </summary>
public class InfrastructureException : TnziException
{
    /// <summary>
    /// 基础设施组件名称
    /// </summary>
    public string ComponentName { get; }

    /// <summary>
    /// 是否可重试
    /// </summary>
    public bool IsRetryable { get; }

    /// <summary>
    /// 初始化一个<see cref="InfrastructureException"/>类型的新实例
    /// </summary>
    /// <param name="componentName">基础设施组件名称</param>
    /// <param name="message">异常消息</param>
    /// <param name="isRetryable">是否可重试</param>
    /// <param name="innerException">内部异常</param>
    public InfrastructureException(
        string componentName,
        string message,
        bool isRetryable = false,
        Exception? innerException = null)
        : base($"{componentName.ToUpperInvariant()}_ERROR", message, innerException)
    {
        ComponentName = componentName;
        IsRetryable = isRetryable;
    }
}

/// <summary>
/// 配置异常（属于基础设施异常）
/// </summary>
public class ConfigurationException : InfrastructureException
{
    /// <summary>
    /// 配置键
    /// </summary>
    public string? ConfigurationKey { get; }

    /// <summary>
    /// 初始化一个<see cref="ConfigurationException"/>类型的新实例
    /// </summary>
    /// <param name="message">异常消息</param>
    public ConfigurationException(string message)
        : base("Configuration", message, false)
    {
    }

    /// <summary>
    /// 初始化一个<see cref="ConfigurationException"/>类型的新实例
    /// </summary>
    /// <param name="configKey">配置键</param>
    /// <param name="message">异常消息</param>
    public ConfigurationException(string configKey, string message)
        : base("Configuration", message, false)
    {
        ConfigurationKey = configKey;
        if (configKey != null)
        {
            this.WithData("ConfigurationKey", configKey);
        }
    }
}
