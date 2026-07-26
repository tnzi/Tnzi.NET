namespace Tnzi.Storage.Services;

/// <summary>
/// 文件版本管理服务实现
/// </summary>
public class FileVersionService : ApplicationService, IFileVersionService
{
    private readonly IRepository<FileVersion, Guid> _versionRepository;
    private readonly IRepository<FileRecord, Guid> _fileRepository;
    private readonly IFileStorage _storage;
    private readonly IFileAccessAuthorizer _accessAuthorizer;

    public FileVersionService(
        IRepository<FileVersion, Guid> versionRepository,
        IRepository<FileRecord, Guid> fileRepository,
        IFileStorage storage,
        IFileAccessAuthorizer accessAuthorizer,
        IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        _versionRepository = Check.NotNull(versionRepository);
        _fileRepository = Check.NotNull(fileRepository);
        _storage = Check.NotNull(storage);
        _accessAuthorizer = Check.NotNull(accessAuthorizer);
    }

    /// <summary>
    /// 载入文件记录并校验访问权限。不通过一律以 404 返回,不泄露该 id 上是否有文件。
    /// </summary>
    private async Task<(FileRecord? Record, Result<T>? Denied)> LoadAuthorizedAsync<T>(
        Guid fileId,
        bool forWrite,
        CancellationToken cancellationToken)
    {
        var fileRecord = await _fileRepository.GetAsync(fileId, cancellationToken);
        if (fileRecord == null)
            return (null, Fail<T>("File not found", 404, ErrorCodes.RESOURCE_NOT_FOUND));

        var allowed = forWrite
            ? await _accessAuthorizer.CanWriteAsync(fileRecord, cancellationToken)
            : await _accessAuthorizer.CanReadAsync(fileRecord, cancellationToken);

        return allowed
            ? (fileRecord, null)
            : (null, Fail<T>("File not found", 404, ErrorCodes.RESOURCE_NOT_FOUND));
    }

    public async Task<Result<FileVersion>> CreateVersionAsync(Guid fileId, Stream stream, string? description = null, CancellationToken cancellationToken = default)
    {
        var (fileRecord, denied) = await LoadAuthorizedAsync<FileVersion>(fileId, forWrite: true, cancellationToken);
        if (denied != null)
            return denied;

        // 获取当前最大版本号
        var maxVersion = await _versionRepository.AsQueryable()
            .Where(v => v.FileId == fileId)
            .Select(v => (int?)v.Version)
            .MaxAsync(cancellationToken) ?? 0;

        // 首次版本化时 maxVersion=0，v1 由下方逻辑创建（当前文件快照），v2 为新上传版本
        var newVersion = maxVersion + 1;
        if (newVersion < 2) newVersion = 2;

        // 保存当前版本（如果还没有保存）
        var currentVersion = await _versionRepository
            .FirstOrDefaultAsync(v => v.FileId == fileId && v.IsCurrent, cancellationToken);

        if (currentVersion == null)
        {
            // 保存当前文件为版本
            currentVersion = new FileVersion
            {
                FileId = fileId,
                Version = 1,
                Path = fileRecord.Path ?? string.Empty,
                Size = fileRecord.Size,
                Md5Hash = fileRecord.Md5Hash,
                Description = "Initial version",
                IsCurrent = false,
                CreationTime = fileRecord.CreationTime,
                CreatorId = fileRecord.CreatorId
            };
            await _versionRepository.InsertAsync(currentVersion, cancellationToken);
        }
        else
        {
            // 将当前版本标记为非当前
            currentVersion.IsCurrent = false;
            await _versionRepository.UpdateAsync(currentVersion, cancellationToken);
        }

        // 计算新版本的MD5
        var md5Hash = await Md5Helper.CalculateAsync(stream);
        stream.Position = 0;

        // 上传新版本文件
        var versionFileName = $"{fileRecord.FileName}.v{newVersion}";
        var filePath = await _storage.UploadAsync(versionFileName, stream, fileRecord.ContentType);

        // 创建新版本记录
        var newVersionRecord = new FileVersion
        {
            FileId = fileId,
            Version = newVersion,
            Path = filePath,
            Size = stream.Length,
            Md5Hash = md5Hash,
            Description = description,
            IsCurrent = true
        };

        await _versionRepository.InsertAsync(newVersionRecord, cancellationToken);

        // 更新文件记录的路径和大小
        fileRecord.Path = filePath;
        fileRecord.Size = stream.Length;
        fileRecord.Md5Hash = md5Hash;
        await _fileRepository.UpdateAsync(fileRecord, cancellationToken);

        LogInformation("File version created: FileId: {FileId}, Version: {Version}", fileId, newVersion);
        return Ok(newVersionRecord, $"File version {newVersion} created successfully");
    }

    public async Task<Result<IEnumerable<FileVersion>>> GetVersionsAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        var (_, denied) = await LoadAuthorizedAsync<IEnumerable<FileVersion>>(fileId, forWrite: false, cancellationToken);
        if (denied != null)
            return denied;

        var versions = await _versionRepository.AsQueryable()
            .Where(v => v.FileId == fileId)
            .OrderByDescending(v => v.Version)
            .ToListAsync(cancellationToken);

        return Ok((IEnumerable<FileVersion>)versions);
    }

    public async Task<Result<FileRecord>> RestoreVersionAsync(Guid fileId, int version, CancellationToken cancellationToken = default)
    {
        var (fileRecord, denied) = await LoadAuthorizedAsync<FileRecord>(fileId, forWrite: true, cancellationToken);
        if (denied != null)
            return denied;

        var targetVersion = await _versionRepository.AsQueryable()
            .Where(v => v.FileId == fileId && v.Version == version)
            .FirstOrDefaultAsync(cancellationToken);

        if (targetVersion == null)
            return Fail<FileRecord>($"Version {version} not found for file {fileId}", 404, ErrorCodes.RESOURCE_NOT_FOUND);

        // 将当前版本标记为非当前
        var currentVersion = await _versionRepository.AsQueryable()
            .Where(v => v.FileId == fileId && v.IsCurrent)
            .FirstOrDefaultAsync(cancellationToken);

        if (currentVersion != null)
        {
            currentVersion.IsCurrent = false;
            await _versionRepository.UpdateAsync(currentVersion, cancellationToken);
        }

        // 将目标版本标记为当前
        targetVersion.IsCurrent = true;
        await _versionRepository.UpdateAsync(targetVersion, cancellationToken);

        // 更新文件记录
        fileRecord.Path = targetVersion.Path;
        fileRecord.Size = targetVersion.Size;
        fileRecord.Md5Hash = targetVersion.Md5Hash;
        await _fileRepository.UpdateAsync(fileRecord, cancellationToken);

        LogInformation("File version restored: FileId: {FileId}, Version: {Version}", fileId, version);
        return Ok(fileRecord, $"File restored to version {version}");
    }

    public async Task<Result<Stream>> GetVersionContentAsync(Guid fileId, int version, CancellationToken cancellationToken = default)
    {
        var (_, denied) = await LoadAuthorizedAsync<Stream>(fileId, forWrite: false, cancellationToken);
        if (denied != null)
            return denied;

        var targetVersion = await _versionRepository.AsQueryable()
            .Where(v => v.FileId == fileId && v.Version == version)
            .FirstOrDefaultAsync(cancellationToken);

        if (targetVersion == null)
            return Fail<Stream>($"Version {version} not found for file {fileId}", 404, ErrorCodes.RESOURCE_NOT_FOUND);

        if (string.IsNullOrEmpty(targetVersion.Path))
            return Fail<Stream>($"Version {version} has no stored content for file {fileId}", 404, ErrorCodes.RESOURCE_NOT_FOUND);

        var stream = await _storage.DownloadAsync(targetVersion.Path);
        return Ok(stream);
    }

    public async Task<Result> DeleteVersionAsync(Guid fileId, int version, CancellationToken cancellationToken = default)
    {
        var (_, denied) = await LoadAuthorizedAsync<object>(fileId, forWrite: true, cancellationToken);
        if (denied != null)
            return denied;

        // 用 tracking 查询，确保 DeleteAsync 操作的是已被上下文跟踪的实例，避免身份映射冲突
        var targetVersion = await _versionRepository.AsQueryable(withTracking: true)
            .Where(v => v.FileId == fileId && v.Version == version)
            .FirstOrDefaultAsync(cancellationToken);

        if (targetVersion == null)
            return Fail($"Version {version} not found for file {fileId}", 404, ErrorCodes.RESOURCE_NOT_FOUND);

        if (targetVersion.IsCurrent)
            return Fail("Cannot delete the current version", 400, ErrorCodes.FILE_OPERATION_ERROR);

        // 先删数据库记录，再删物理文件（物理删除失败仅告警，不阻断）
        await _versionRepository.DeleteAsync(targetVersion, cancellationToken);

        if (!string.IsNullOrEmpty(targetVersion.Path))
        {
            try
            {
                await _storage.DeleteAsync(targetVersion.Path);
            }
            catch (Exception ex)
            {
                LogWarning("Failed to delete physical file for version. FileId: {FileId}, Version: {Version}, Path: {Path}, Error: {Error}",
                    fileId, version, targetVersion.Path, ex.Message);
            }
        }

        LogInformation("File version deleted: FileId: {FileId}, Version: {Version}", fileId, version);
        return Ok($"File version {version} deleted successfully");
    }
}
