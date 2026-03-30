namespace Tnzi.Audit.Options;

/// <summary>
/// 审计模块配置选项
/// </summary>
public class AuditOptions
{
    /// <summary>是否启用操作审计</summary>
    public bool EnableOperationAudit { get; set; } = true;

    /// <summary>是否启用实体变更审计</summary>
    public bool EnableEntityAudit { get; set; } = true;

    /// <summary>审计数据保留天数</summary>
    public int RetentionDays { get; set; } = 90;

    /// <summary>批量处理大小</summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>排除的请求路径前缀</summary>
    public string[] ExcludedPaths { get; set; } = ["/swagger", "/health", "/scalar"];

    /// <summary>Channel 容量 (0 = 无限)</summary>
    public int ChannelCapacity { get; set; } = 10000;

    /// <summary>是否记录请求参数</summary>
    public bool EnableRequestParameters { get; set; } = true;

    /// <summary>是否记录响应结果</summary>
    public bool EnableResponseResult { get; set; } = false;

    /// <summary>是否启用请求体记录</summary>
    public bool EnableRequestBodyCapture { get; set; } = false;

    /// <summary>请求体最大记录大小（字节），超出部分截断</summary>
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
