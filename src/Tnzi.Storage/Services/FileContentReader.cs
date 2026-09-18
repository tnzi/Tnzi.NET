namespace Tnzi.Storage.Services;

/// <summary>
/// <see cref="IFileContentReader"/> 的默认实现：按记录找到物理对象，直接从 provider 读。
/// </summary>
/// <remarks>
/// ★ 刻意<b>不</b>经 <c>IFileStorageService.GetAsync</c>：那条路过 <c>IFileAccessAuthorizer</c>，
/// 是「这个人读这份文件」的路径，在没有当前用户的后台里对私密文件必然 404。
/// 本实现是「系统读这份文件」，授权发生在 id 被接受的那一刻（<see cref="IFileReadAccessProbe"/>），
/// 不在这里重复。记录查询仍走仓储，所以多租户过滤器照常生效：别的租户的文件在这里也是「不存在」。
/// </remarks>
public class FileContentReader : IFileContentReader
{
    private readonly IReadOnlyRepository<FileRecord, Guid> _records;
    private readonly IFileStorage _storage;
    private readonly ILogger<FileContentReader> _logger;

    public FileContentReader(
        IReadOnlyRepository<FileRecord, Guid> records,
        IFileStorage storage,
        ILogger<FileContentReader> logger)
    {
        _records = Check.NotNull(records);
        _storage = Check.NotNull(storage);
        _logger = Check.NotNull(logger);
    }

    public async Task<Stream?> OpenReadAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        if (fileId == Guid.Empty)
            return null;

        var record = await _records.FindAsync(fileId, cancellationToken);
        if (record == null || string.IsNullOrEmpty(record.Path))
            return null;

        if (!await _storage.ExistsAsync(record.Path))
        {
            // 记录在、对象不在：不是「没有这个文件」而是存储侧丢了它，值得一条警告。
            _logger.LogWarning("File record {FileId} points at {Path}, but the object is missing from {Provider}.",
                fileId, record.Path, _storage.ProviderName);
            return null;
        }

        return await _storage.DownloadAsync(record.Path);
    }
}
