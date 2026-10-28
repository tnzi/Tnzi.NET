
namespace Tnzi.AspNetCore.Options;

/// <summary>
/// 异常处理选项
/// </summary>
[ConfigSection("AspNetCore:ExceptionHandling")]
[RuntimeSettingGroup(Key = "web-observability", Module = "Web", DisplayName = "Request Observability",
    I18nKey = "admin.modules.system.settings.groups.webObservability",
    Icon = "mdi:chart-timeline-variant", Order = 700, PermissionGroup = "system")]
public class ExceptionHandlingOptions
{
    /// <summary>
    /// 是否在开发环境暴露详细错误
    /// </summary>
    [RuntimeSetting(Label = "Show Details In Development", I18n = "admin.modules.system.settings.fields.showDetailsInDevelopment",
        Type = SettingFieldType.Boolean)]
    public bool ShowDetailsInDevelopment { get; set; } = true;

    /// <summary>
    /// 是否在未处理异常发生时记录请求体（可能包含敏感数据）
    /// </summary>
    [RuntimeSetting(Label = "Log Request Body On Exception", I18n = "admin.modules.system.settings.fields.exceptionLogRequestBody",
        Type = SettingFieldType.Boolean,
        Description = "When enabled, the request body is captured and written to the server log alongside any unhandled exception. Useful for diagnosing failures, but the body may contain sensitive data (passwords, tokens) - keep disabled in production unless needed. Bodies are capped at 8 KB and only buffered when this is on.")]
    public bool LogRequestBody { get; set; } = false;

    /// <summary>
    /// 自定义异常处理器（异常类型 -> 处理器类型）。
    /// </summary>
    /// <remarks>
    /// 只能在代码里经 <c>Configure&lt;ExceptionHandlingOptions&gt;</c> 登记，配置文件绑定不了以 <see cref="Type"/> 为键的字典。
    /// 登记在基类上的处理器也处理其子类，离抛出类型最近的登记优先；处理器返回 null 或
    /// <see cref="ExceptionHandlingResult.ShouldContinueHandling"/> 为 true 时交给内置处理器链。
    /// </remarks>
    public Dictionary<Type, Type> CustomHandlers { get; set; } = new();

    /// <summary>
    /// 是否启用异常统计
    /// </summary>
    public bool EnableMetrics { get; set; } = true;

    /// <summary>
    /// 是否在错误响应里带 <c>requestId</c>：与 <c>X-Request-Id</c> 响应头同一个值（没有请求追踪中间件时退回连接级的 TraceIdentifier）
    /// </summary>
    [RuntimeSetting(Label = "Include Request ID In Response", I18n = "admin.modules.system.settings.fields.includeRequestId",
        Type = SettingFieldType.Boolean)]
    public bool IncludeRequestId { get; set; } = true;

    /// <summary>
    /// 异常统计在内存里保留的最近记录条数。
    /// </summary>
    /// <remarks>
    /// 它同时是诊断摘要的上界：「最近 N 分钟的异常」只在这些记录里按时间窗口筛，
    /// 所以窗口内的异常多于这个数时，摘要里的总数与各项计数停在这个数附近，不是窗口内的真实总量。
    /// 需要准确的长期计数请看日志或指标系统。
    /// </remarks>
    public int ExceptionHistorySize { get; set; } = 100;

    /// <summary>
    /// 是否在响应中包含上下文数据
    /// </summary>
    [RuntimeSetting(Label = "Include Context Data In Response", I18n = "admin.modules.system.settings.fields.exceptionIncludeContextData",
        Type = SettingFieldType.Boolean,
        Description = "When enabled, BusinessException/InfrastructureException ContextData is included in error responses. This can expose internal identifiers or state - review carefully and prefer disabling in production.")]
    public bool IncludeContextData { get; set; } = true;

    /// <summary>
    /// 是否在响应中包含重试信息
    /// </summary>
    public bool IncludeRetryInfo { get; set; } = true;
}