
namespace Tnzi.Options;

/// <summary>
/// Options 验证器基类
/// 简化验证器实现，确保验证逻辑高效
/// 性能提示：保持验证逻辑简单，避免复杂计算、网络调用或数据库查询
/// </summary>
/// <typeparam name="TOptions">配置选项类型</typeparam>
public abstract class OptionsValidatorBase<TOptions> : IValidateOptions<TOptions>
    where TOptions : class
{
    /// <summary>
    /// 日志记录器，用于输出验证警告信息
    /// 子类可通过构造函数注入 ILoggerFactory 来启用警告日志
    /// </summary>
    protected ILogger Logger { get; }

    /// <summary>
    /// 默认构造函数（不输出警告日志）
    /// </summary>
    protected OptionsValidatorBase()
    {
        Logger = NullLogger.Instance;
    }

    /// <summary>
    /// 带日志工厂的构造函数，启用验证警告日志输出
    /// </summary>
    /// <param name="loggerFactory">日志工厂（为 null 时使用 NullLogger）</param>
    protected OptionsValidatorBase(ILoggerFactory? loggerFactory)
    {
        Logger = loggerFactory?.CreateLogger(GetType()) ?? NullLogger.Instance;
    }

    /// <summary>
    /// 验证配置选项
    /// 性能提示：保持验证逻辑简单，避免复杂计算
    /// </summary>
    /// <param name="name">配置名称</param>
    /// <param name="options">配置选项</param>
    /// <returns>验证结果</returns>
    public ValidateOptionsResult Validate(string? name, TOptions options)
    {
        if (options == null)
        {
            var configPath = GetConfigurationPath(name);
            return ValidateOptionsResult.Fail($"{configPath} cannot be null.");
        }

        var errors = new List<string>();
        ValidateOptions(options, errors);

        // 收集并输出警告信息（不阻止启动）
        var warnings = new List<string>();
        CollectWarnings(options, warnings);
        if (warnings.Count > 0)
        {
            var configPath = GetConfigurationPath(name);
            foreach (var warning in warnings)
            {
                var enhancedWarning = warning.StartsWith(configPath, StringComparison.OrdinalIgnoreCase)
                    ? warning
                    : $"{configPath}: {warning}";
                Logger.LogWarning("Options validation warning: {Warning}", enhancedWarning);
            }
        }

        if (errors.Count > 0)
        {
            // 增强错误信息，包含配置路径
            var configPath = GetConfigurationPath(name);
            var enhancedErrors = errors.Select(error =>
                error.StartsWith(configPath, StringComparison.OrdinalIgnoreCase)
                    ? error
                    : $"{configPath}: {error}").ToList();
            return ValidateOptionsResult.Fail(enhancedErrors);
        }

        return ValidateOptionsResult.Success;
    }

    /// <summary>
    /// 获取配置路径（用于错误消息）
    /// </summary>
    /// <param name="name">配置名称</param>
    /// <returns>配置路径字符串</returns>
    protected virtual string GetConfigurationPath(string? name)
    {
        var optionsTypeName = typeof(TOptions).Name;
        // 移除 "Options" 后缀（如果存在）
        var configSection = optionsTypeName.EndsWith("Options", StringComparison.OrdinalIgnoreCase)
            ? optionsTypeName.Substring(0, optionsTypeName.Length - 7)
            : optionsTypeName;

        if (!string.IsNullOrEmpty(name) && name != Microsoft.Extensions.Options.Options.DefaultName)
        {
            return $"{configSection}[{name}]";
        }

        return configSection;
    }

    /// <summary>
    /// 添加验证错误（带配置路径和期望值信息）
    /// </summary>
    /// <param name="errors">错误列表</param>
    /// <param name="propertyName">属性名称</param>
    /// <param name="message">错误消息</param>
    /// <param name="expectedValue">期望值（可选）</param>
    protected void AddError(List<string> errors, string propertyName, string message, object? expectedValue = null)
    {
        var errorMessage = string.IsNullOrEmpty(propertyName)
            ? message
            : $"{propertyName}: {message}";

        if (expectedValue != null)
        {
            errorMessage += $" Expected: {expectedValue}.";
        }

        errors.Add(errorMessage);
    }

    /// <summary>
    /// 子类实现验证逻辑
    /// 性能提示：只验证关键配置项，避免复杂业务逻辑
    /// </summary>
    /// <param name="options">配置选项</param>
    /// <param name="errors">错误列表</param>
    protected abstract void ValidateOptions(TOptions options, List<string> errors);

    /// <summary>
    /// 收集验证警告（不阻止启动，仅通过日志输出）
    /// 子类可重写此方法来报告非致命的配置问题
    /// </summary>
    /// <param name="options">配置选项</param>
    /// <param name="warnings">警告列表</param>
    protected virtual void CollectWarnings(TOptions options, List<string> warnings)
    {
    }

    /// <summary>
    /// 添加验证警告（带属性名称）
    /// </summary>
    /// <param name="warnings">警告列表</param>
    /// <param name="propertyName">属性名称</param>
    /// <param name="message">警告消息</param>
    protected void AddWarning(List<string> warnings, string propertyName, string message)
    {
        var warningMessage = string.IsNullOrEmpty(propertyName)
            ? message
            : $"{propertyName}: {message}";
        warnings.Add(warningMessage);
    }
}