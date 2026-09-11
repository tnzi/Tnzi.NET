namespace Tnzi.Feature.Services;

/// <summary>
/// 后台侧的用量记录读取口，供 <see cref="FeatureUsageBackgroundService"/> 成批消费。
/// </summary>
public interface IFeatureUsageConsumer
{
    /// <summary>
    /// 队列读取端。
    /// </summary>
    ChannelReader<FeatureUsageRecord> Reader { get; }
}
