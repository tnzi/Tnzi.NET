namespace Tnzi.Storage.Workspace.Events.Handlers;

/// <summary>
/// 父模块删掉一条文件记录之后，收掉它在工作区里的历史版本与分享链接（见 <see cref="DeletedFileWorkspaceCleaner"/>）。
/// </summary>
/// <remarks>
/// 不包 try/catch：版本对象删不掉时以异常离开，事件总线的重试与死信才会发生；整体吞掉再记日志等于告诉总线「处理成功」。
/// 重试用尽的那些由后台清理的贡献者兜底（它按「版本行在、记录不在」找，不依赖这个事件）。
/// </remarks>
public class FileRecordDeletedEventHandler : IEventHandler<FileRecordDeletedEvent>
{
    private readonly DeletedFileWorkspaceCleaner _cleaner;

    public FileRecordDeletedEventHandler(DeletedFileWorkspaceCleaner cleaner)
    {
        _cleaner = Check.NotNull(cleaner);
    }

    public async Task HandleAsync(FileRecordDeletedEvent @event, CancellationToken cancellationToken = default)
    {
        Check.NotNull(@event);
        await _cleaner.PurgeAsync(@event.FileId, cancellationToken);
    }
}
