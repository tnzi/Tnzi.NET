namespace Tnzi.Performance.Options;

/// <summary>
/// 性能分析配置选项
/// </summary>
public class PerformanceOptions
{
    /// <summary>
    /// 获取或设置 是否启用性能分析
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// 获取或设置 慢请求阈值（毫秒）
    /// </summary>
    public double? SlowRequestThresholdMs { get; set; } = 1000;

    /// <summary>
    /// 获取或设置 最大历史记录数量
    /// </summary>
    public int MaxHistorySize { get; set; } = 1000;

    /// <summary>
    /// 获取或设置 是否记录请求大小
    /// </summary>
    public bool RecordRequestSize { get; set; } = true;

    /// <summary>
    /// 获取或设置 是否记录响应大小
    /// </summary>
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
