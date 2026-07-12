namespace Tnzi.Performance.Options;

/// <summary>
/// 性能分析配置选项
/// </summary>
[ConfigSection("Performance")]
[RuntimeSettingGroup(Key = "web-performance", Module = "Web", DisplayName = "Performance Monitoring",
    I18nKey = "admin.modules.system.settings.groups.webPerformance",
    Icon = "mdi:speedometer", Order = 730, PermissionGroup = "system")]
public class PerformanceOptions
{
    /// <summary>
    /// 获取或设置 是否启用性能分析。
    /// 默认启用（opt-out）：加载 PerformanceModule 本身已是显式选择，
    /// 默认关闭会使模块加载后零采样（admin Performance 页恒为空）。
    /// 采集为内存环形缓冲（MaxHistorySize 上限），开销可忽略；
    /// 如需关闭，配置 Performance:Enabled = false。
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 获取或设置 慢请求阈值（毫秒）
    /// </summary>
    [RuntimeSetting(Label = "Slow Request Threshold (ms)", I18n = "admin.modules.system.settings.fields.performanceSlowThreshold",
        Type = SettingFieldType.Int, Min = 1,
        Description = "Requests slower than this threshold (in milliseconds) are logged as slow-request warnings.")]
    public double? SlowRequestThresholdMs { get; set; } = 1000;

    /// <summary>
    /// 获取或设置 最大历史记录数量
    /// </summary>
    public int MaxHistorySize { get; set; } = 1000;

    /// <summary>
    /// 获取或设置 是否记录请求大小
    /// </summary>
    [RuntimeSetting(Label = "Record Request Size", I18n = "admin.modules.system.settings.fields.performanceRecordRequestSize",
        Type = SettingFieldType.Boolean,
        Description = "Record the request content length for each tracked request.")]
    public bool RecordRequestSize { get; set; } = true;

    /// <summary>
    /// 获取或设置 是否记录响应大小
    /// </summary>
    [RuntimeSetting(Label = "Record Response Size", I18n = "admin.modules.system.settings.fields.performanceRecordResponseSize",
        Type = SettingFieldType.Boolean,
        Description = "Record the response byte count for each tracked request (wraps the response stream in a counting stream).")]
    public bool RecordResponseSize { get; set; } = true;

    /// <summary>
    /// 获取或设置 排除的路径列表（不记录性能数据）
    /// </summary>
    public string[]? ExcludePaths { get; set; }

    /// <summary>
    /// 获取或设置 包含的路径列表（仅记录这些路径的性能数据，如果为空则记录所有路径）
    /// </summary>
    public string[]? IncludePaths { get; set; }
}
