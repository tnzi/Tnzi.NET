namespace Tnzi.AspNetCore.Settings;

/// <summary>
/// AspNetCore 模块内置配置定义 — 观测性、安全头部、限流三组。
/// 仅收录经 IOptionsMonitor.CurrentValue 运行时热消费的标量字段：
///   ExceptionHandlingMiddleware / RequestTrackingMiddleware / SecurityHeadersMiddleware 均为热消费；
///   RateLimitingMiddleware 已改造为 IOptionsMonitor 热消费。
/// 绑定节：
///   RequestTracking → AspNetCore:RequestTracking (独立 IOptionsMonitor&lt;RequestTrackingOptions&gt;)
///   ExceptionHandling → AspNetCore:ExceptionHandling (独立 IOptionsMonitor&lt;ExceptionHandlingOptions&gt;)
///   SecurityHeaders / RateLimit → AspNetCore:SecurityHeaders / AspNetCore:RateLimit (AspNetCoreOptions 子节)
/// </summary>
public class AspNetCoreSettingDefinitionProvider : ISettingDefinitionProvider
{
    private const string I18nBase = "admin.modules.system.settings";

    public IReadOnlyList<SettingDefinitionGroup> GetGroups() =>
    [
        new SettingDefinitionGroup
        {
            Key = "web-observability",
            ModuleName = "Web",
            DisplayName = "Request Observability",
            I18nKey = $"{I18nBase}.groups.webObservability",
            Icon = "mdi:chart-timeline-variant",
            Order = 700,
            Fields =
            [
                // RequestTrackingOptions — binds to AspNetCore:RequestTracking
                Field("AspNetCore:RequestTracking:EnableRequestLogging", "Enable Request Logging", "enableRequestLogging",
                    SettingFieldType.Boolean, () => new RequestTrackingOptions().EnableRequestLogging.ToString().ToLowerInvariant()),
                Field("AspNetCore:RequestTracking:LogRequestBody", "Log Request Body", "logRequestBody",
                    SettingFieldType.Boolean, () => new RequestTrackingOptions().LogRequestBody.ToString().ToLowerInvariant()),
                Field("AspNetCore:RequestTracking:LogResponseBody", "Log Response Body", "logResponseBody",
                    SettingFieldType.Boolean, () => new RequestTrackingOptions().LogResponseBody.ToString().ToLowerInvariant()),
                Field("AspNetCore:RequestTracking:MaxRequestBodyLength", "Max Request Body Length (bytes)", "maxRequestBodyLength",
                    SettingFieldType.Int, () => new RequestTrackingOptions().MaxRequestBodyLength.ToString(CultureInfo.InvariantCulture),
                    min: 0),
                Field("AspNetCore:RequestTracking:MaxResponseBodyLength", "Max Response Body Length (bytes)", "maxResponseBodyLength",
                    SettingFieldType.Int, () => new RequestTrackingOptions().MaxResponseBodyLength.ToString(CultureInfo.InvariantCulture),
                    min: 0),
                Field("AspNetCore:RequestTracking:SlowRequestThresholdMs", "Slow Request Threshold (ms)", "slowRequestThresholdMs",
                    SettingFieldType.Int, defaultAccessor: null, min: 0),
                // ExceptionHandlingOptions — binds to AspNetCore:ExceptionHandling
                Field("AspNetCore:ExceptionHandling:ShowDetailsInDevelopment", "Show Details In Development", "showDetailsInDevelopment",
                    SettingFieldType.Boolean, () => new ExceptionHandlingOptions().ShowDetailsInDevelopment.ToString().ToLowerInvariant()),
                Field("AspNetCore:ExceptionHandling:IncludeRequestId", "Include Request ID In Response", "includeRequestId",
                    SettingFieldType.Boolean, () => new ExceptionHandlingOptions().IncludeRequestId.ToString().ToLowerInvariant()),
                // EnableMetrics 不收录：它只在启动期门控 IExceptionStatistics 的 DI 注册，
                // 中间件运行时从不读取 — 收录即死字段。
            ],
        },
        new SettingDefinitionGroup
        {
            Key = "web-security-headers",
            ModuleName = "Web",
            DisplayName = "Security Headers",
            I18nKey = $"{I18nBase}.groups.webSecurityHeaders",
            Icon = "mdi:shield-lock-outline",
            Order = 710,
            Fields =
            [
                // SecurityHeadersOptions — binds to AspNetCore:SecurityHeaders
                Field("AspNetCore:SecurityHeaders:EnableSecurityHeaders", "Enable Security Headers", "enableSecurityHeaders",
                    SettingFieldType.Boolean, () => new SecurityHeadersOptions().EnableSecurityHeaders.ToString().ToLowerInvariant()),
                Field("AspNetCore:SecurityHeaders:ContentSecurityPolicy", "Content-Security-Policy", "contentSecurityPolicy",
                    SettingFieldType.Text, () => new SecurityHeadersOptions().ContentSecurityPolicy),
                Field("AspNetCore:SecurityHeaders:XFrameOptions", "X-Frame-Options", "xFrameOptions",
                    SettingFieldType.String, () => new SecurityHeadersOptions().XFrameOptions),
                Field("AspNetCore:SecurityHeaders:XContentTypeOptions", "X-Content-Type-Options", "xContentTypeOptions",
                    SettingFieldType.String, () => new SecurityHeadersOptions().XContentTypeOptions),
                Field("AspNetCore:SecurityHeaders:XXssProtection", "X-XSS-Protection", "xXssProtection",
                    SettingFieldType.String, () => new SecurityHeadersOptions().XXssProtection),
                Field("AspNetCore:SecurityHeaders:ReferrerPolicy", "Referrer-Policy", "referrerPolicy",
                    SettingFieldType.String, () => new SecurityHeadersOptions().ReferrerPolicy),
                Field("AspNetCore:SecurityHeaders:HstsEnabled", "Enable HSTS", "hstsEnabled",
                    SettingFieldType.Boolean, () => new SecurityHeadersOptions().HstsEnabled.ToString().ToLowerInvariant()),
                Field("AspNetCore:SecurityHeaders:HstsMaxAge", "HSTS Max-Age (seconds)", "hstsMaxAge",
                    SettingFieldType.Int, () => new SecurityHeadersOptions().HstsMaxAge.ToString(CultureInfo.InvariantCulture),
                    min: 0),
            ],
        },
        new SettingDefinitionGroup
        {
            Key = "web-ratelimit",
            ModuleName = "Web",
            DisplayName = "Rate Limiting",
            I18nKey = $"{I18nBase}.groups.webRatelimit",
            Icon = "mdi:speedometer",
            Order = 720,
            Fields =
            [
                // RateLimitOptions — binds to AspNetCore:RateLimit
                Field("AspNetCore:RateLimit:Enabled", "Enable Rate Limiting", "rateLimitEnabled",
                    SettingFieldType.Boolean, () => new RateLimitOptions().Enabled.ToString().ToLowerInvariant()),
                Field("AspNetCore:RateLimit:DefaultLimit", "Default Limit (requests)", "rateLimitDefaultLimit",
                    SettingFieldType.Int, () => new RateLimitOptions().DefaultLimit.ToString(CultureInfo.InvariantCulture),
                    min: 1),
                Field("AspNetCore:RateLimit:DefaultWindowSeconds", "Default Window (seconds)", "rateLimitDefaultWindowSeconds",
                    SettingFieldType.Int, () => new RateLimitOptions().DefaultWindowSeconds.ToString(CultureInfo.InvariantCulture),
                    min: 1),
                Field("AspNetCore:RateLimit:AllowOnFailure", "Allow On Failure (fail-open)", "rateLimitAllowOnFailure",
                    SettingFieldType.Boolean, () => new RateLimitOptions().AllowOnFailure.ToString().ToLowerInvariant()),
            ],
        },
    ];

    private static SettingFieldDefinition Field(
        string key,
        string label,
        string i18nSuffix,
        SettingFieldType type = SettingFieldType.String,
        Func<string?>? defaultAccessor = null,
        int? min = null,
        int? max = null) => new()
    {
        Key = key,
        Label = label,
        I18nKey = $"{I18nBase}.fields.{i18nSuffix}",
        Type = type,
        DefaultValueAccessor = defaultAccessor,
        Min = min,
        Max = max,
    };
}
