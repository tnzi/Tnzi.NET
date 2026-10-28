namespace Tnzi.AI.Channels.Events.Handlers;

/// <summary>
/// 线程被删除时，删掉指向它的 IM 会话映射。
/// </summary>
/// <remarks>
/// 映射只在会话还没有线程时写入，之后一直原样读出。不清掉的话，被删线程所在的会话下一条消息会被运行以
/// 「线程不存在」拒绝（随后由 <see cref="StaleThreadMapping"/> 自愈）；这里在删除那一刻就把映射收走，
/// 不必等到下一条消息。删除按 <c>ThreadId</c> 幂等，失败让异常冒泡给事件总线（重试与死信由总线负责）。
/// </remarks>
public class ChannelThreadMappingCleanupHandler : IEventHandler<ThreadDeletedEvent>
{
    private readonly IChannelThreadStore _threadStore;
    private readonly ICurrentTenant? _currentTenant;

    public ChannelThreadMappingCleanupHandler(IChannelThreadStore threadStore, ICurrentTenant? currentTenant = null)
    {
        _threadStore = Check.NotNull(threadStore);
        _currentTenant = currentTenant;
    }

    public async Task HandleAsync(ThreadDeletedEvent evt, CancellationToken ct = default)
    {
        Check.NotNull(evt);

        // 映射行是 IMultiTenant：在线程所属的租户里删（发布时随事件捕获），而不是处理器恰好所在的租户
        using var tenantScope = _currentTenant?.Change(evt.TenantId);
        await _threadStore.RemoveByThreadIdAsync(evt.ThreadId);
    }
}
