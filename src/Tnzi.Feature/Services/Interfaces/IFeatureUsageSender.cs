namespace Tnzi.Feature.Services;

/// <summary>
/// 请求侧的用量记录投递口：把一条 <see cref="FeatureUsageRecord"/> 放进内存队列就返回，
/// 落库由 <see cref="FeatureUsageBackgroundService"/> 在后台成批完成。
/// </summary>
/// <remarks>
/// 此前每次 <see cref="IFeatureChecker.IsEnabledAsync"/> 都同步 <c>INSERT</c> 一行 ——
/// 一个 <c>[RequireFeature]</c> 端点因此每请求多一次写库往返。用量是遥测不是业务数据，
/// 队列满时丢弃并计数，绝不阻塞请求。
/// </remarks>
public interface IFeatureUsageSender
{
    /// <summary>
    /// 非阻塞投递。队列满时返回 <c>false</c>（记录被丢弃并计入丢弃数）。
    /// </summary>
    bool TrySend(FeatureUsageRecord record);
}
