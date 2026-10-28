namespace Tnzi.Storage.Services;

/// <summary>
/// <see cref="IStorageQuotaService"/> 的默认实现：用量 = 名下文件记录的字节数 + 各贡献者报的数，配额由 <see cref="IStorageAllowanceProvider"/> 给。
/// </summary>
/// <remarks>
/// ★ 先问配额再算用量：没有配额（默认）时一条查询都不发。上传是热路径，而绝大多数部署不设配额。
/// </remarks>
public class StorageQuotaService : IStorageQuotaService
{
    private readonly IRepository<FileRecord, Guid> _files;
    private readonly IStorageAllowanceProvider _allowances;
    private readonly IReadOnlyList<IStorageUsageContributor> _contributors;

    public StorageQuotaService(
        IRepository<FileRecord, Guid> files,
        IStorageAllowanceProvider allowances,
        IEnumerable<IStorageUsageContributor> contributors)
    {
        _files = Check.NotNull(files);
        _allowances = Check.NotNull(allowances);
        _contributors = Check.NotNull(contributors).ToArray();
    }

    public async Task<StorageQuotaDto> GetQuotaAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var records = await _files.AsQueryable()
            .Where(f => f.CreatorId == userId)
            .GroupBy(f => f.CreatorId)
            .Select(g => new { Count = g.Count(), Bytes = g.Sum(f => f.Size) })
            .FirstOrDefaultAsync(cancellationToken);

        return new StorageQuotaDto
        {
            UserId = userId,
            FileCount = records?.Count ?? 0,
            UsedBytes = (records?.Bytes ?? 0) + await GetContributedBytesAsync(userId, cancellationToken),
            AllowanceBytes = await _allowances.GetAllowanceAsync(userId, cancellationToken)
        };
    }

    public async Task<long> GetUsedBytesAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var recordBytes = await _files.AsQueryable()
            .Where(f => f.CreatorId == userId)
            .SumAsync(f => (long?)f.Size, cancellationToken) ?? 0;

        return recordBytes + await GetContributedBytesAsync(userId, cancellationToken);
    }

    public async Task<Result<T>?> EnsureCapacityAsync<T>(Guid? ownerId, long additionalBytes, CancellationToken cancellationToken = default)
    {
        if (ownerId is not { } userId || additionalBytes <= 0)
            return null;

        if (await _allowances.GetAllowanceAsync(userId, cancellationToken) is not { } allowance)
            return null;

        var used = await GetUsedBytesAsync(userId, cancellationToken);
        if (used + additionalBytes <= allowance)
            return null;

        return Result.Failure<T>(
            $"Storage quota exceeded: this needs {additionalBytes} more bytes, but {used} of the {allowance} bytes allowed are already used. Free up space or ask an administrator for a larger allowance.",
            StatusCodes.Status413PayloadTooLarge,
            ErrorCodes.FILE_QUOTA_EXCEEDED,
            new StorageQuotaExceededDetails { UsedBytes = used, RequestedBytes = additionalBytes, AllowanceBytes = allowance });
    }

    private async Task<long> GetContributedBytesAsync(Guid userId, CancellationToken cancellationToken)
    {
        long total = 0;
        foreach (var contributor in _contributors)
        {
            total += await contributor.GetUsedBytesAsync(userId, cancellationToken);
        }

        return total;
    }
}
