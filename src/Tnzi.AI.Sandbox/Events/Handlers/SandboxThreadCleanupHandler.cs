namespace Tnzi.AI.Sandbox.Events.Handlers;

/// <summary>
/// 线程删除时清掉它在沙箱里留下的东西：线程目录（<c>workspace/</c>、<c>uploads/</c>、<c>outputs/</c>、技能资源副本与哨兵）
/// 以及这条线程的配额计数。
/// </summary>
/// <remarks>
/// <para>
/// ★ 此前线程删除只清数据库行（核心的级联清理处理器），线程目录永远留在磁盘上：用户删掉的对话里上传的合同、agent 生成的文件
/// 与约 2 MB 的技能副本一直留着，磁盘只增不减，而用户以为这些内容已经删了。
/// </para>
/// <para>
/// 删除不跟进链接（见 <see cref="SandboxFileSystem.DeleteTree"/>）：agent 能在线程目录里建指向任意位置的链接，跟进去就是替它删宿主文件。
/// 目录不存在即完成，总线重试安全。真实的 IO 失败不吞，冒泡给事件总线走重试与死信。
/// </para>
/// </remarks>
public class SandboxThreadCleanupHandler : IEventHandler<ThreadDeletedEvent>
{
    private readonly IVirtualPathTranslator _translator;
    private readonly ILogger<SandboxThreadCleanupHandler> _logger;
    private readonly IThreadResourceQuota? _quota;

    public SandboxThreadCleanupHandler(
        IVirtualPathTranslator translator,
        ILogger<SandboxThreadCleanupHandler> logger,
        IThreadResourceQuota? quota = null)
    {
        _translator = Check.NotNull(translator);
        _logger = Check.NotNull(logger);
        _quota = quota;
    }

    public async Task HandleAsync(ThreadDeletedEvent @event, CancellationToken cancellationToken = default)
    {
        Check.NotNull(@event);

        var threadDirectory = _translator.GetThreadDirectory(@event.ThreadId);
        if (Directory.Exists(threadDirectory) || SandboxFileSystem.IsLink(threadDirectory))
        {
            SandboxFileSystem.DeleteTree(threadDirectory);
            _logger.LogInformation("Removed the sandbox directory of deleted thread {ThreadId}", @event.ThreadId);
        }

        if (_quota is not null)
            await _quota.ResetAsync(@event.ThreadId, cancellationToken);
    }
}
