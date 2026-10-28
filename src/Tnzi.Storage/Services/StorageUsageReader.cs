namespace Tnzi.Storage.Services;

/// <summary>
/// 核心 <see cref="IStorageUsageReader"/> 的实现：一个范围的文件记录字节 + 缩略图字节 + 各 <see cref="IStorageUsageContributor"/> 报的字节。
/// </summary>
/// <remarks>
/// <para>
/// 口径与存储配额相同（见 <see cref="StorageQuotaService"/>）：文件记录不分临时与否、回收站里尚未清除的照算；
/// 系统渲染的产物（发票 PDF 这类）也是文件记录，没有创建人，所以只进整个安装与租户两种范围。
/// 多出来的一项是缩略图，配额从来不算它，计费要算（它同样占存储）。
/// </para>
/// <para>
/// ★ 缩略图的大小在生成时没有记录。这里先把这个范围里还没量过的缩略图向存储提供者量一批
/// （<see cref="ThumbnailMeasureBatchSize"/> 个，写回 <see cref="FileRecord.ThumbnailSize"/>），再求和；
/// 量不完的下一次接着量。对象已不存在的记 0（它不再占存储）。量的时候出错的留着不记，下一次再试。
/// </para>
/// <para>
/// 整个安装的范围<b>越过租户过滤</b>：按安装计费的账户覆盖的就是全部存储。
/// </para>
/// </remarks>
public class StorageUsageReader : IStorageUsageReader
{
    /// <summary>一次调用最多量多少个缩略图（每个一次提供者往返）。</summary>
    public const int ThumbnailMeasureBatchSize = 500;

    private readonly IRepository<FileRecord, Guid> _files;
    private readonly IFileStorage _storage;
    private readonly IReadOnlyList<IStorageUsageContributor> _contributors;
    private readonly ILogger<StorageUsageReader> _logger;

    public StorageUsageReader(
        IRepository<FileRecord, Guid> files,
        IFileStorage storage,
        IEnumerable<IStorageUsageContributor> contributors,
        ILogger<StorageUsageReader> logger)
    {
        _files = Check.NotNull(files);
        _storage = Check.NotNull(storage);
        _contributors = Check.NotNull(contributors).ToArray();
        _logger = Check.NotNull(logger);
    }

    public async Task<long> GetStoredBytesAsync(StorageUsageScope scope, StorageUsageReadMode mode = StorageUsageReadMode.Measure, CancellationToken cancellationToken = default)
    {
        Check.NotNull(scope);

        if (mode == StorageUsageReadMode.Measure)
            await MeasureThumbnailsAsync(scope, cancellationToken);

        var totals = await InScope(scope)
            .GroupBy(_ => 1)
            .Select(g => new { Files = g.Sum(f => (long?)f.Size), Thumbnails = g.Sum(f => f.ThumbnailSize) })
            .FirstOrDefaultAsync(cancellationToken);

        long total = (totals?.Files ?? 0) + (totals?.Thumbnails ?? 0);
        foreach (var contributor in _contributors)
            total += await contributor.GetUsedBytesAsync(scope, cancellationToken);

        return total;
    }

    private IQueryable<FileRecord> InScope(StorageUsageScope scope) => scope.Kind switch
    {
        StorageUsageScopeKind.Installation => _files.AsQueryable().IgnoreQueryFilters(),
        StorageUsageScopeKind.Tenant => _files.AsQueryable().IgnoreQueryFilters().Where(f => f.TenantId == scope.Id),
        StorageUsageScopeKind.User => _files.AsQueryable().Where(f => f.CreatorId == scope.Id),
        _ => throw new ArgumentOutOfRangeException(nameof(scope), scope.Kind, "Unknown storage usage scope.")
    };

    private async Task MeasureThumbnailsAsync(StorageUsageScope scope, CancellationToken cancellationToken)
    {
        var pending = await InScope(scope)
            .Where(f => f.ThumbnailPath != null && f.ThumbnailSize == null)
            .OrderBy(f => f.Id)
            .Take(ThumbnailMeasureBatchSize)
            .Select(f => new { f.Id, f.ThumbnailPath })
            .ToListAsync(cancellationToken);

        foreach (var record in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();

            long size;
            try
            {
                size = await _storage.ExistsAsync(record.ThumbnailPath!)
                    ? await _storage.GetFileSizeAsync(record.ThumbnailPath!)
                    : 0;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Could not measure the thumbnail of file {FileId}; it is left out of storage usage until a later sample measures it.", record.Id);
                continue;
            }

            // 只在它仍是量的那一张时写：量的过程中缩略图可能被换掉了
            var path = record.ThumbnailPath;
            await _files.AsQueryable().IgnoreQueryFilters()
                .Where(f => f.Id == record.Id && f.ThumbnailPath == path && f.ThumbnailSize == null)
                .ExecuteUpdateAsync(s => s.SetProperty(f => f.ThumbnailSize, size), cancellationToken);
        }
    }
}
