namespace Tnzi.Storage.Workspace.Services;

/// <summary>
/// 文件版本管理服务实现
/// </summary>
public class FileVersionService : ApplicationService, IFileVersionService
{
    private readonly IRepository<FileVersion, Guid> _versionRepository;
    private readonly IRepository<FileRecord, Guid> _fileRepository;
    private readonly IFileStorage _storage;
    private readonly IFileAccessAuthorizer _accessAuthorizer;
    private readonly UploadGuard _guard;

    public FileVersionService(
        IRepository<FileVersion, Guid> versionRepository,
        IRepository<FileRecord, Guid> fileRepository,
        IFileStorage storage,
        IFileAccessAuthorizer accessAuthorizer,
        IServiceProvider serviceProvider,
        UploadGuard guard)
        : base(serviceProvider)
    {
        _versionRepository = Check.NotNull(versionRepository);
        _fileRepository = Check.NotNull(fileRepository);
        _storage = Check.NotNull(storage);
        _accessAuthorizer = Check.NotNull(accessAuthorizer);
        _guard = Check.NotNull(guard);
    }

    /// <summary>
    /// 载入文件记录并校验访问权限。不存在与不允许都返回 null,调用方一律以 404 回应,
    /// 不泄露该 id 上是否有文件。
    /// </summary>
    private async Task<FileRecord?> LoadAuthorizedAsync(Guid fileId, bool forWrite, CancellationToken cancellationToken)
    {
        var fileRecord = await _fileRepository.GetAsync(fileId, cancellationToken);
        if (fileRecord == null)
            return null;

        var allowed = forWrite
            ? await _accessAuthorizer.CanWriteAsync(fileRecord, cancellationToken)
            : await _accessAuthorizer.CanReadAsync(fileRecord, cancellationToken);

        return allowed ? fileRecord : null;
    }

    /// <summary>
    /// 版本记录里的 Size：优先用上传**之前**测得的长度；测不出来（不可 seek 的净化替换流）时
    /// 回问 provider。两者都拿不到就记 0 并留一条 Warning —— 与 <c>FileStorageService</c> 同口径。
    /// </summary>
    private async Task<long> ResolveStoredSizeAsync(long? knownSize, string filePath)
    {
        if (knownSize.HasValue)
            return knownSize.Value;

        try
        {
            return await _storage.GetFileSizeAsync(filePath);
        }
        catch (Exception ex)
        {
            LogWarning("Unable to resolve stored size for {FilePath}, recording 0: {Error}", filePath, ex.Message);
            return 0L;
        }
    }

    private Result<T> FileNotFound<T>() => Fail<T>("File not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);

    private Result FileNotFound() => Fail("File not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);

    public async Task<Result<FileVersionDto>> CreateVersionAsync(Guid fileId, Stream stream, string? description = null, CancellationToken cancellationToken = default)
    {
        var fileRecord = await LoadAuthorizedAsync(fileId, forWrite: true, cancellationToken);
        if (fileRecord == null)
            return FileNotFound<FileVersionDto>();

        // ★ 净化管线跑在**最前面**，早于下面写 v1 快照那一步：拒绝发生在写任何一行之前，
        //   否则一次被拒的上传会留下一条凭空多出来的「Initial version」并把当前版本标成非当前。
        //   判据用父记录声明的扩展名与内容类型 —— 那才是净化器要评判的「自称是什么」，
        //   存储键只是个服务端生成的落地名。
        //
        //   这条路径此前完全不过净化：先正常传一张干净的 png，再对它 POST 一个新版本，
        //   字节就换成了没被扫过的内容，而记录上仍写着 .png / image/png。
        await using var sanitized = await _guard.RunAsync(
            fileRecord.OriginalName, fileRecord.Extension, fileRecord.ContentType, stream);

        if (sanitized.IsRejected)
            return Fail<FileVersionDto>(sanitized.Reason!, 400, ErrorCodes.VALIDATION_ERROR);

        var content = sanitized.Content;

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

        // 计算新版本的MD5（对**净化之后**的字节算，落库的哈希才与落库的内容一致）
        if (content.CanSeek) content.Position = 0;
        var md5Hash = await HashHelper.GetMd5Async(content);
        if (content.CanSeek) content.Position = 0;

        // 上传新版本文件。长度必须在把流交给 provider **之前**取：上传之后这个流是否还可读
        // 由 provider 决定（见 IFileStorage.UploadAsync 的流所有权约定）。
        // ★ 量的是**净化之后**那份字节（净化器可以重编码）；净化器交回的替换流不保证可 seek，
        //   量不出来就回问 provider，由它按已落盘的对象报大小 —— 与 FileStorageService
        //   的 ResolveStoredSizeAsync 同一条兜底：大小是描述性字段，不该在文件已经存好之后
        //   把整次保存变成一次失败。
        long? knownSize = content.CanSeek ? content.Length : null;
        // ★ 版本键也由服务端生成，**不**从 fileRecord.FileName 派生：2026-09-04 之前有三条写路径
        //   把调用方给的名字写进了 FileName，派生等于把那份污染再传一代（见 StorageKeyHelper）。
        var versionFileName = StorageKeyHelper.NewKey(fileRecord.Extension);
        var filePath = await _storage.UploadAsync(versionFileName, content, fileRecord.ContentType);
        var size = await ResolveStoredSizeAsync(knownSize, filePath);

        // 创建新版本记录
        var newVersionRecord = new FileVersion
        {
            FileId = fileId,
            Version = newVersion,
            Path = filePath,
            Size = size,
            Md5Hash = md5Hash,
            Description = description,
            IsCurrent = true
        };

        await _versionRepository.InsertAsync(newVersionRecord, cancellationToken);

        // 更新文件记录的路径和大小
        fileRecord.Path = filePath;
        fileRecord.Size = size;
        fileRecord.Md5Hash = md5Hash;
        await _fileRepository.UpdateAsync(fileRecord, cancellationToken);

        LogInformation("File version created: FileId: {FileId}, Version: {Version}", fileId, newVersion);
        return Ok(MapToDto(newVersionRecord), $"File version {newVersion} created successfully");
    }

    public async Task<Result<IEnumerable<FileVersionDto>>> GetVersionsAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        if (await LoadAuthorizedAsync(fileId, forWrite: false, cancellationToken) == null)
            return FileNotFound<IEnumerable<FileVersionDto>>();

        var versions = await _versionRepository.AsQueryable()
            .Where(v => v.FileId == fileId)
            .OrderByDescending(v => v.Version)
            .ToListAsync(cancellationToken);

        return Ok(versions.Select(MapToDto).AsEnumerable());
    }

    /// <summary>
    /// 投影为对外 DTO：<b>绝不暴露 Path / Md5Hash / TenantId</b>。
    /// 这层投影此前在控制器里逐个 <c>MapTo</c>，契约改成 DTO 之后收进服务，
    /// 序列化出去的字段一字未变。
    /// </summary>
    private static FileVersionDto MapToDto(FileVersion version) => new()
    {
        Id = version.Id,
        FileId = version.FileId,
        Version = version.Version,
        Size = version.Size,
        Description = version.Description,
        IsCurrent = version.IsCurrent,
        CreationTime = version.CreationTime,
        CreatorId = version.CreatorId
    };

    public async Task<Result<FileRecord>> RestoreVersionAsync(Guid fileId, int version, CancellationToken cancellationToken = default)
    {
        var fileRecord = await LoadAuthorizedAsync(fileId, forWrite: true, cancellationToken);
        if (fileRecord == null)
            return FileNotFound<FileRecord>();

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
        if (await LoadAuthorizedAsync(fileId, forWrite: false, cancellationToken) == null)
            return FileNotFound<Stream>();

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
        if (await LoadAuthorizedAsync(fileId, forWrite: true, cancellationToken) == null)
            return FileNotFound();

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
