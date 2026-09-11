namespace Tnzi.Notification.Push.Metadata;

/// <summary>
/// 匿名设备端点用到的请求头名。
/// </summary>
/// <remarks>
/// 住在 <c>Metadata/</c> 而不是控制器内部：客户端要照着它发请求，是公开契约，
/// 按 docs/coding-standards/metadata.md 的分界属于「共享/公开」那一类。
/// </remarks>
public static class PushDeviceHeaders
{
    /// <summary>
    /// 匿名设备密钥所在的请求头。
    /// </summary>
    /// <remarks>
    /// ★ <b>走请求头而不是查询串</b>，与令牌走请求体是同一条理由：查询串会原样进访问日志
    /// 与反向代理日志，而这个值是这台设备的<b>全部</b>凭据 —— 拿到它就能改挂它的推送地址。
    /// <para>
    /// 不走请求体是因为注销那条没有别的载荷，而「为了藏一个值而造一个只有一个字段的请求体」
    /// 会让三个端点的取值方式各不相同；取值方式不一致的地方，就是以后有人漏取的地方。
    /// </para>
    /// </remarks>
    public const string DeviceKey = "X-Device-Key";
}
