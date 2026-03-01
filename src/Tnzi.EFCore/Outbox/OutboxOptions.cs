
namespace Tnzi.EFCore.Outbox;

/// <summary>
/// Outbox 模式配置选项
/// 配置路径：EFCore:Outbox
/// </summary>
public class OutboxOptions
{
    /// <summary>
    /// 是否启用 Outbox 模式
    /// 默认值：true
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 轮询间隔（秒）
    /// 默认值：10
    /// </summary>
    public int PollingIntervalSeconds { get; set; } = 10;

    /// <summary>
    /// 最大重试次数（超过后标记为已处理，避免无限重试）
    /// 默认值：5
    /// </summary>
    public int MaxRetryCount { get; set; } = 5;

    /// <summary>
    /// 每批处理的消息数量
    /// 默认值：100
    /// </summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// 已处理消息保留天数（超过后自动删除）
    /// 默认值：90
    /// </summary>
    public int RetentionDays { get; set; } = 90;
}
