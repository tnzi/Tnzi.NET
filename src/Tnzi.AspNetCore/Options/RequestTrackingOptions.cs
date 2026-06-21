
namespace Tnzi.AspNetCore.Options;

/// <summary>
/// 请求追踪选项
/// </summary>
[ConfigSection("AspNetCore:RequestTracking")]
[RuntimeSettingGroup(Key = "web-observability", Module = "Web", DisplayName = "Request Observability",
    I18nKey = "admin.modules.system.settings.groups.webObservability",
    Icon = "mdi:chart-timeline-variant", Order = 700)]
public class RequestTrackingOptions
{
    /// <summary>
    /// 是否启用请求日志
    /// </summary>
    [RuntimeSetting(Label = "Enable Request Logging", I18n = "admin.modules.system.settings.fields.enableRequestLogging",
        Type = SettingFieldType.Boolean)]
    public bool EnableRequestLogging { get; set; } = true;

    /// <summary>
    /// 日志级别（Debug, Information, Warning, Error）
    /// </summary>
    public LogLevel LogLevel { get; set; } = LogLevel.Information;

    /// <summary>
    /// 是否记录请求体
    /// </summary>
    [RuntimeSetting(Label = "Log Request Body", I18n = "admin.modules.system.settings.fields.logRequestBody",
        Type = SettingFieldType.Boolean)]
    public bool LogRequestBody { get; set; } = false;

    /// <summary>
    /// 是否记录响应体
    /// </summary>
    [RuntimeSetting(Label = "Log Response Body", I18n = "admin.modules.system.settings.fields.logResponseBody",
        Type = SettingFieldType.Boolean)]
    public bool LogResponseBody { get; set; } = false;

    /// <summary>
    /// 请求体最大记录长度（字节）
    /// </summary>
    [RuntimeSetting(Label = "Max Request Body Length (bytes)", I18n = "admin.modules.system.settings.fields.maxRequestBodyLength",
        Type = SettingFieldType.Int, Min = 0)]
    public int MaxRequestBodyLength { get; set; } = 1024;

    /// <summary>
    /// 响应体最大记录长度（字节）
    /// </summary>
    [RuntimeSetting(Label = "Max Response Body Length (bytes)", I18n = "admin.modules.system.settings.fields.maxResponseBodyLength",
        Type = SettingFieldType.Int, Min = 0)]
    public int MaxResponseBodyLength { get; set; } = 1024;

    /// <summary>
    /// 是否记录响应时间
    /// </summary>
    public bool LogResponseTime { get; set; } = true;

    /// <summary>
    /// 慢请求阈值（毫秒）
    /// </summary>
    [RuntimeSetting(Label = "Slow Request Threshold (ms)", I18n = "admin.modules.system.settings.fields.slowRequestThresholdMs",
        Type = SettingFieldType.Int, Min = 0)]
    public int? SlowRequestThresholdMs { get; set; }

    /// <summary>
    /// 需要记录的路径模式（* 匹配任意, ? 匹配单个）
    /// </summary>
    public List<string>? IncludePaths { get; set; }

    /// <summary>
    /// 不需要记录的路径模式
    /// 默认排除：/health, /metrics, /favicon.ico, /swagger, /api-docs
    /// 设置后将覆盖默认值
    /// </summary>
    public List<string>? ExcludePaths { get; set; }
}