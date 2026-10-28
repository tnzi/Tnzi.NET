namespace Tnzi.Storage.Services;

/// <summary>
/// <see cref="IFileHoldService"/> 的默认实现。计数与删除都走 <see cref="FileReferenceCounts"/> 的条件语句。
/// </summary>
public class FileHoldService : ApplicationService, IFileHoldService
{
    private readonly IRepository<FileRecord, Guid> _fileRepository;
    private readonly IRepository<FileReference, Guid> _referenceRepository;
    private readonly IFileStorage _storage;

    public FileHoldService(
        IServiceProvider serviceProvider,
        IRepository<FileRecord, Guid> fileRepository,
        IRepository<FileReference, Guid> referenceRepository,
        IFileStorage storage)
        : base(serviceProvider)
    {
        _fileRepository = Check.NotNull(fileRepository);
        _referenceRepository = Check.NotNull(referenceRepository);
        _storage = Check.NotNull(storage);
    }

    public async Task<FileReleaseOutcome> ReleaseImplicitHoldsAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        FileRecord? deleted = null;
        var outcome = await ExecuteInUnitOfWorkAsync(async token =>
        {
            var record = await _fileRepository.GetAsync(fileId, token);
            if (record == null)
                return FileReleaseOutcome.NotFound;

            await FileReferenceCounts.ReleaseImplicitHoldsAsync(_fileRepository, _referenceRepository, fileId, token);

            // 与用户自己的删除同一口径：归档进目录的存量临时记录的那条豁免是给自动回收的，不拦这里。
            if (!await FileReferenceCounts.TryDeleteUnheldAsync(_fileRepository, _referenceRepository, fileId, token, exemptFiledTemporaries: false))
                return FileReleaseOutcome.StillReferenced;

            deleted = record;
            return FileReleaseOutcome.Deleted;
        }, cancellationToken);

        if (deleted != null)
        {
            await DeletedFileObjects.DisposeAsync(_storage, EventBus, Logger, deleted, cancellationToken);

            if (EventBus != null)
                await EventBus.PublishAsync(new FileRecordDeletedEvent { FileId = fileId }, cancellationToken);

            LogInformation("File {FileId} released by its owner and deleted", fileId);
        }

        return outcome;
    }
}
