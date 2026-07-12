namespace Tnzi.Audit.Options;

/// <summary>
/// 审计模块配置选项
/// </summary>
[ConfigSection("Audit")]
[RuntimeSettingGroup(Key = "audit-retention", Module = "Audit", DisplayName = "Retention",
    I18nKey = "admin.modules.system.settings.groups.auditRetention",
    Icon = "mdi:archive-clock-outline", Order = 600)]
public class AuditOptions
{
    /// <summary>是否启用操作审计</summary>
    [RuntimeSetting(Label = "Operation Audit", I18n = "admin.modules.system.settings.fields.auditEnableOperation",
        Type = SettingFieldType.Boolean, Subsection = "Capture",
        Description = "Master switch for API operation audit logging (request/response metadata)")]
    public bool EnableOperationAudit { get; set; } = true;

    /// <summary>
    /// 是否启用实体变更审计。
    /// KEEP-STATIC：当前无运行时消费者（EF 拦截器未接线读取此开关），暴露会造成"假热配"。
    /// </summary>
    public bool EnableEntityAudit { get; set; } = true;

    /// <summary>审计数据保留天数</summary>
    [RuntimeSetting(Label = "Retention Days", I18n = "admin.modules.system.settings.fields.retentionDays",
        Type = SettingFieldType.Int, Min = 1)]
    public int RetentionDays { get; set; } = 90;

    /// <summary>批量处理大小</summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// 排除的请求路径前缀。
    /// "/hubs" 必须排除：SignalR WebSocket/SSE 经 access_token 查询参数携带 JWT，
    /// 而审计记录的 Url 字段存 Path + QueryString，不排除会把完整令牌持久化进审计表；
    /// 且 WS 长连接在断开时才入队，产生耗时数小时的无意义"操作"记录。
    /// </summary>
    public string[] ExcludedPaths { get; set; } = ["/swagger", "/health", "/scalar", "/hubs"];

    /// <summary>Channel 容量 (0 = 无限)</summary>
    public int ChannelCapacity { get; set; } = 10000;

    /// <summary>是否记录请求参数</summary>
    [RuntimeSetting(Label = "Capture Request Parameters", I18n = "admin.modules.system.settings.fields.auditEnableRequestParameters",
        Type = SettingFieldType.Boolean, Subsection = "Capture",
        Description = "Record query string / form parameters on each audited operation")]
    public bool EnableRequestParameters { get; set; } = true;

    /// <summary>
    /// 是否记录响应结果。
    /// KEEP-STATIC：当前无运行时消费者（AuditMiddleware 未读取此开关），暴露会造成"假热配"。
    /// </summary>
    public bool EnableResponseResult { get; set; } = false;

    /// <summary>是否启用请求体记录</summary>
    [RuntimeSetting(Label = "Capture Request Body", I18n = "admin.modules.system.settings.fields.auditEnableRequestBodyCapture",
        Type = SettingFieldType.Boolean, Subsection = "Capture",
        Description = "Persist request bodies to the audit table. May store sensitive payloads (PII, credentials); sensitive fields are redacted, but review privacy impact before enabling in production")]
    public bool EnableRequestBodyCapture { get; set; } = false;

    /// <summary>请求体最大记录大小（字节），超出部分截断</summary>
    [RuntimeSetting(Label = "Max Request Body Size (bytes)", I18n = "admin.modules.system.settings.fields.auditMaxRequestBodySize",
        Type = SettingFieldType.Int, Min = 1, Max = 65536, Subsection = "Capture",
        Description = "Request bodies larger than this size (bytes) are truncated")]
    public int MaxRequestBodySize { get; set; } = 4096;

    /// <summary>需要脱敏的敏感字段名（不区分大小写）</summary>
    public HashSet<string> SensitiveFields { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "password",
        "token",
        "secret",
        "credential",
        "authorization",
        "accessToken",
        "refreshToken",
        "apiKey",
        "connectionString",
        "creditCard"
    };
}
