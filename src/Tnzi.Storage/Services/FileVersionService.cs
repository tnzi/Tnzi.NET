namespace Tnzi.Storage.Services;

/// <summary>
/// 文件版本管理服务实现
/// </summary>
public class FileVersionService : ApplicationService, IFileVersionService
{
    private readonly IRepository<FileVersion, Guid> _versionRepository;
    private readonly IRepository<FileRecord, Guid> _fileRepository;
    private readonly IFileStorage _storage;

    public FileVersionService(
        IRepository<FileVersion, Guid> versionRepository,
        IRepository<FileRecord, Guid> fileRepository,
        IFileStorage storage,
        IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        _versionRepository = Check.NotNull(versionRepository);
        _fileRepository = Check.NotNull(fileRepository);
        _storage = Check.NotNull(storage);
    }

    public async Task<Result<FileVersion>> CreateVersionAsync(Guid fileId, Stream stream, string? description = null, CancellationToken cancellationToken = default)
    {
        var fileRecord = await _fileRepository.GetAsync(fileId, cancellationToken);
        if (fileRecord == null)
            return Fail<FileVersion>("File not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);

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
        var versions = await _versionRepository.AsQueryable()
            .Where(v => v.FileId == fileId)
            .OrderByDescending(v => v.Version)
            .ToListAsync(cancellationToken);

        return Ok((IEnumerable<FileVersion>)versions);
    }

    public async Task<Result<FileRecord>> RestoreVersionAsync(Guid fileId, int version, CancellationToken cancellationToken = default)
    {
        var fileRecord = await _fileRepository.GetAsync(fileId, cancellationToken);
        if (fileRecord == null)
            return Fail<FileRecord>("File not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);

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
}
