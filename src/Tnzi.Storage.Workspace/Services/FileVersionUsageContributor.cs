namespace Tnzi.Storage.Workspace.Services;

/// <summary>
/// 把历史版本占的字节算进配额用量：一个文件的<b>非当前</b>版本各自有一份对象，属于文件的所有者。
/// </summary>
/// <remarks>
/// 当前版本的对象就是文件记录指向的那一份，父模块已经按记录的大小算过，这里不重复。
/// 不算历史版本，一个用户可以靠反复建新版本绕过配额。
/// </remarks>
public class FileVersionUsageContributor : IStorageUsageContributor
{
    private readonly IRepository<FileVersion, Guid> _versionRepository;
    private readonly IRepository<FileRecord, Guid> _fileRepository;

    public FileVersionUsageContributor(IRepository<FileVersion, Guid> versionRepository, IRepository<FileRecord, Guid> fileRepository)
    {
        _versionRepository = Check.NotNull(versionRepository);
        _fileRepository = Check.NotNull(fileRepository);
    }

    public async Task<long> GetUsedBytesAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var ownedFiles = _fileRepository.AsQueryable().Where(f => f.CreatorId == userId);
        return await _versionRepository.AsQueryable()
            .Where(v => !v.IsCurrent && ownedFiles.Any(f => f.Id == v.FileId))
            .SumAsync(v => (long?)v.Size, cancellationToken) ?? 0;
    }

    /// <summary>
    /// 一个范围里历史版本占的字节：整个安装与租户两种范围<b>越过租户过滤</b>按版本行本身算，用户范围同上一个重载。
    /// </summary>
    public async Task<long> GetUsedBytesAsync(StorageUsageScope scope, CancellationToken cancellationToken = default)
    {
        Check.NotNull(scope);

        if (scope is { Kind: StorageUsageScopeKind.User, Id: { } userId })
            return await GetUsedBytesAsync(userId, cancellationToken);

        var versions = _versionRepository.AsQueryable().IgnoreQueryFilters().Where(v => !v.IsCurrent);
        if (scope.Kind == StorageUsageScopeKind.Tenant)
            versions = versions.Where(v => v.TenantId == scope.Id);

        return await versions.SumAsync(v => (long?)v.Size, cancellationToken) ?? 0;
    }
}
