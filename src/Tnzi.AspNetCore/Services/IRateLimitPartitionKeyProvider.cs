namespace Tnzi.AspNetCore.Services;

/// <summary>
/// 限流分区键提供者：决定一个请求算在谁的额度上。
/// </summary>
/// <remarks>
/// <para>
/// <strong>可选能力。</strong>框架不注册任何实现，没有实现时限流沿用内置判定
/// （已登录用户按用户、匿名请求按来源地址），行为与本契约引入之前完全一致。
/// </para>
/// <para>
/// <strong>为什么需要它。</strong>内置判定对匿名请求只有来源地址一个维度可用，
/// 而有些系统<em>刻意不采集来源地址</em>（见 <c>AspNetCoreOptions.CollectClientIpAddress</c>）。
/// 两者叠加的结果是匿名端点没有分区键，限流会按
/// <see cref="RateLimitOptions.MissingPartitionKey"/> 处置——默认是放行。
/// 需要在不采集地址的前提下仍然限流的部署，在这里给出自己的分区维度，
/// 例如一张一次性、短时效、不含身份的提交票据。
/// </para>
/// <para>
/// <strong>返回值只是分区标识，不含路径。</strong>限流键由框架用
/// <c>{分区标识}:{路径}</c> 拼装，实现方不必也不应该自己拼路径，
/// 否则同一个调用方在不同端点上的额度会被错误地合并或拆分。
/// </para>
/// <para>
/// <strong>不要在这里返回可识别个人的值。</strong>分区键会进入限流存储（通常是缓存），
/// 也会出现在超限告警里。在匿名场景下，一个能反查到人的分区键等于把匿名性
/// 从主数据路径挪到了限流路径上——那正是这个契约要避免的事。
/// </para>
/// </remarks>
[ExperimentalApi(Reason = "分区来源的形态仍在演进，可能补充异步解析或分区元数据")]
public interface IRateLimitPartitionKeyProvider
{
    /// <summary>
    /// 多个提供者时的询问顺序，小的先问。
    /// </summary>
    int Order => 0;

    /// <summary>
    /// 给出当前请求的分区标识。
    /// </summary>
    /// <param name="context">HTTP 上下文。</param>
    /// <returns>
    /// 分区标识；返回 <c>null</c> 或空串表示<strong>本提供者无法判定</strong>，
    /// 框架会继续问下一个，全部无法判定时回退内置判定。
    /// </returns>
    /// <remarks>
    /// 必须是同步且廉价的：它在每个受限流的请求上都会被调用。
    /// 需要查库或调远端的判定，应当在更早的中间件里完成并把结果放进
    /// <c>HttpContext.Items</c>，这里只做读取。
    /// </remarks>
    string? GetPartitionKey(HttpContext context);
}
