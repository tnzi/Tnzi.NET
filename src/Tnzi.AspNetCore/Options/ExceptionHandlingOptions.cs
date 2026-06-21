
namespace Tnzi.AspNetCore.Options;

/// <summary>
/// 异常处理选项
/// </summary>
[ConfigSection("AspNetCore:ExceptionHandling")]
[RuntimeSettingGroup(Key = "web-observability", Module = "Web", DisplayName = "Request Observability",
    I18nKey = "admin.modules.system.settings.groups.webObservability",
    Icon = "mdi:chart-timeline-variant", Order = 700)]
public class ExceptionHandlingOptions
{
    /// <summary>
    /// 是否在开发环境暴露详细错误
    /// </summary>
    [RuntimeSetting(Label = "Show Details In Development", I18n = "admin.modules.system.settings.fields.showDetailsInDevelopment",
        Type = SettingFieldType.Boolean)]
    public bool ShowDetailsInDevelopment { get; set; } = true;

    /// <summary>
    /// 是否记录请求体（可能包含敏感数据）
    /// </summary>
    public bool LogRequestBody { get; set; } = false;

    /// <summary>
    /// 自定义异常处理器（异常类型 -> 处理器类型）
    /// </summary>
    public Dictionary<Type, Type> CustomHandlers { get; set; } = new();

    /// <summary>
    /// 是否启用异常统计
    /// </summary>
    public bool EnableMetrics { get; set; } = true;

    /// <summary>
    /// 是否包含请求 ID 在错误响应中
    /// </summary>
    [RuntimeSetting(Label = "Include Request ID In Response", I18n = "admin.modules.system.settings.fields.includeRequestId",
        Type = SettingFieldType.Boolean)]
    public bool IncludeRequestId { get; set; } = true;

    /// <summary>
    /// 异常历史保留数量（用于重复异常检测）
    /// </summary>
    public int ExceptionHistorySize { get; set; } = 100;

    /// <summary>
    /// 是否在响应中包含上下文数据
    /// </summary>
    public bool IncludeContextData { get; set; } = true;

    /// <summary>
    /// 是否在响应中包含重试信息
    /// </summary>
    public bool IncludeRetryInfo { get; set; } = true;
}