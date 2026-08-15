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

    /// <summary>
    /// 获取或设置 取不到分区键时的处置方式。默认 <see cref="MissingPartitionKeyBehavior.Allow"/>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 分区键的来源依次是：注册的 <see cref="Tnzi.AspNetCore.Services.IRateLimitPartitionKeyProvider"/>、
    /// 已登录用户、来源地址。三者都拿不到时（典型情形：匿名请求 + 部署关闭了地址采集）
    /// 限流无法把请求归到任何额度上，此时按本选项处置。
    /// </para>
    /// <para>
    /// <strong>默认放行是为了兼容，不是因为它更安全。</strong>
    /// 一个悄悄不生效的限流比没有限流更危险，因为配置里写着它在。
    /// 对匿名端点是主要攻击面的系统，应显式配置为
    /// <see cref="MissingPartitionKeyBehavior.Deny"/> 或
    /// <see cref="MissingPartitionKeyBehavior.Global"/>，
    /// 或注册自己的分区键提供者从根上避免这种情形。
    /// </para>
    /// </remarks>
    public MissingPartitionKeyBehavior MissingPartitionKey { get; set; } = MissingPartitionKeyBehavior.Allow;
}

/// <summary>
/// 取不到限流分区键时的处置方式。
/// </summary>
public enum MissingPartitionKeyBehavior
{
    /// <summary>
    /// 放行，不限流。默认值，与本选项引入之前的行为一致。
    /// </summary>
    Allow = 0,

    /// <summary>
    /// 拒绝，返回 429。
    /// </summary>
    /// <remarks>
    /// 最保守的一档：宁可拒绝一个无法归类的请求，也不放过一个不受限的调用方。
    /// 代价是任何分区判定失灵都会立刻表现为面向用户的失败——这正是它的用意，
    /// 失灵应当被看见，而不是变成一条谁也没注意到的放行。
    /// </remarks>
    Deny = 1,

    /// <summary>
    /// 落到按路径的全局额度上。
    /// </summary>
    /// <remarks>
    /// 端点整体共用一个额度。它保证了「总量有上限」，
    /// 但<strong>无法阻止单个调用方占满全部额度</strong>，
    /// 也就意味着一个调用方能把其他所有人挡在外面。
    /// 只适合作为过渡或兜底，不能替代真正的分区。
    /// </remarks>
    Global = 2
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

