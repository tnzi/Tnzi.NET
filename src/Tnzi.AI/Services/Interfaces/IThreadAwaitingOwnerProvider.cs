namespace Tnzi.AI.Services;

/// <summary>
/// 回答「哪些线程此刻在等它的主人」：线程上的一轮停在一个只有主人能回答的地方（一个权限请求、一张等确认的卡片、一个问题），
/// 主人不回答它就不往下走。
/// </summary>
/// <remarks>
/// <para>
/// 线程列表把这样的线程排在最前（<see cref="AgentThreadDto.AwaitingOwner"/>，在跑的其次，其余按主人最近一次发消息的时间），
/// 关注状态（<c>POST threads/attention</c>）也单独数出它们，免得它们沉到更新的对话下面没人看见。
/// </para>
/// <para>
/// 可以注册多个，结果取并集：外部 CLI 子模块注册一个（运行中还没过期、还没回答的权限 / 问题 / 计划请求），
/// 应用自己的确认机制（框架之外的工具确认卡片等）再注册自己的：
/// <c>services.AddScoped&lt;IThreadAwaitingOwnerProvider, MyConfirmationsAwaitingOwner&gt;()</c>。
/// 一个都没有时没有线程在等主人。
/// </para>
/// <para>
/// 在线程服务的请求作用域里调用，但<b>不要</b>依赖当前用户或当前租户：管理端列表跨用户地问，<see cref="ThreadAwaitingOwnerQuery.OwnerId"/>
/// 为 null。返回调用方没问到的线程、或别人的线程都无妨，线程服务只取与它自己的查询相交的部分；漏报才是错。
/// 一个提供者抛错时只记日志、按它什么也没报处理，列表照常返回。
/// </para>
/// <para>
/// 每次列表读取与关注状态轮询都会问一次，查询要便宜：按主人或按给定的线程筛，只看还没了结的那一小撮记录。
/// </para>
/// </remarks>
public interface IThreadAwaitingOwnerProvider
{
    /// <summary>此刻在等主人的线程。</summary>
    /// <param name="query">只问哪位主人、哪些线程。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<IReadOnlyCollection<Guid>> GetThreadIdsAsync(ThreadAwaitingOwnerQuery query, CancellationToken cancellationToken = default);
}

/// <summary>交给 <see cref="IThreadAwaitingOwnerProvider"/> 的查询。</summary>
/// <param name="OwnerId">只问这位主人（线程的创建者）的线程；null = 所有人的。</param>
/// <param name="ThreadIds">只问这些线程；null = 不限（一个空集合不会交给提供者）。</param>
public sealed record ThreadAwaitingOwnerQuery(Guid? OwnerId, IReadOnlyCollection<Guid>? ThreadIds);
