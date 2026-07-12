namespace Tnzi.AspNetCore.Options;

/// <summary>
/// 限流配置选项
/// </summary>
[ConfigSection("AspNetCore:RateLimit")]
[RuntimeSettingGroup(Key = "web-ratelimit", Module = "Web", DisplayName = "Rate Limiting",
    I18nKey = "admin.modules.system.settings.groups.webRatelimit",
    Icon = "mdi:speedometer", Order = 720, PermissionGroup = "system")]
public class RateLimitOptions
{
    /// <summary>
    /// 获取或设置 是否启用限流
    /// </summary>
    [RuntimeSetting(Label = "Enable Rate Limiting", I18n = "admin.modules.system.settings.fields.rateLimitEnabled",
        Type = SettingFieldType.Boolean)]
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// 获取或设置 默认限流数量
    /// </summary>
    [RuntimeSetting(Label = "Default Limit (requests)", I18n = "admin.modules.system.settings.fields.rateLimitDefaultLimit",
        Type = SettingFieldType.Int, Min = 1)]
    public int DefaultLimit { get; set; } = 100;

    /// <summary>
    /// 获取或设置 默认时间窗口（秒）
    /// </summary>
    [RuntimeSetting(Label = "Default Window (seconds)", I18n = "admin.modules.system.settings.fields.rateLimitDefaultWindowSeconds",
        Type = SettingFieldType.Int, Min = 1)]
    public int DefaultWindowSeconds { get; set; } = 60;

    /// <summary>
    /// 获取或设置 基于 IP 的限流配置
    /// </summary>
    public RateLimitRule? ByIp { get; set; }

    /// <summary>
    /// 获取或设置 基于用户的限流配置
    /// </summary>
    public RateLimitRule? ByUser { get; set; }

    /// <summary>
    /// 获取或设置 基于路径的限流配置（路径 -> 限流规则）
    /// </summary>
    public Dictionary<string, RateLimitRule>? ByPath { get; set; }

    /// <summary>
    /// 获取或设置 排除限流的路径列表
    /// </summary>
    public string[]? ExcludePaths { get; set; }

    /// <summary>
    /// 获取或设置 限流服务故障时的处理策略
    /// true: 允许所有请求通过（fail-open）
    /// false: 拒绝所有请求（fail-safe，默认）
    /// </summary>
    [RuntimeSetting(Label = "Allow On Failure (fail-open)", I18n = "admin.modules.system.settings.fields.rateLimitAllowOnFailure",
        Type = SettingFieldType.Boolean)]
    public bool AllowOnFailure { get; set; } = false;
}

/// <summary>
/// 限流规则
/// </summary>
public class RateLimitRule
{
    /// <summary>
    /// 获取或设置 限流数量
    /// </summary>
    public int Limit { get; set; } = 100;

    /// <summary>
    /// 获取或设置 时间窗口（秒）
    /// </summary>
    public int WindowSeconds { get; set; } = 60;

    /// <summary>
    /// 获取或设置 限流算法类型
    /// 默认使用固定窗口算法
    /// </summary>
    public RateLimitAlgorithm Algorithm { get; set; } = RateLimitAlgorithm.FixedWindow;

    /// <summary>
    /// 获取或设置 白名单标识列表（可选）
    /// 如果请求的标识在此列表中，则不受限流限制
    /// </summary>
    public string[]? Whitelist { get; set; }
}

