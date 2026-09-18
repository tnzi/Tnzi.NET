namespace Tnzi.Storage.Services;

/// <summary>
/// 文件存储服务实现（核心文件操作）
/// </summary>
public class FileStorageService : ApplicationService, IFileStorageService
{
    private readonly IRepository<FileRecord, Guid> _repository;
    private readonly IRepository<FileReference, Guid> _referenceRepository;
    private readonly IFileStorage _storage;
    private readonly IOptionsMonitor<StorageOptions> _optionsMonitor;
    private readonly IFileAccessAuthorizer _accessAuthorizer;
    private readonly IPublicFileFieldResolver _publicFieldResolver;
    private readonly IFileUrlSigner _urlSigner;

    /// <summary>
    /// 上传净化管线。<strong>未注册任何净化器时为空集合</strong>，
    /// 整条管线不执行、不读流、零开销——这是该能力保持可选的方式。
    /// </summary>
    private readonly UploadGuard _guard;

    /// <summary>
    /// 缩略图生成器：位图与 PDF 首页两条出图路径、以及「这种文件此刻画不画得出来」的判定都在它那里。
    /// 四条会产出缩略图的写路径与存量回填共用同一份。
    /// </summary>
    private readonly IFileThumbnailGenerator _thumbnails;

    private StorageOptions Options => _optionsMonitor.CurrentValue;

    public FileStorageService(
        IRepository<FileRecord, Guid> repository,
        IRepository<FileReference, Guid> referenceRepository,
        IFileStorage storage,
        IOptionsMonitor<StorageOptions> optionsMonitor,
        IFileAccessAuthorizer accessAuthorizer,
        IPublicFileFieldResolver publicFieldResolver,
        IFileUrlSigner urlSigner,
        IServiceProvider serviceProvider,
        UploadGuard guard,
        IFileThumbnailGenerator thumbnails)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
        _referenceRepository = Check.NotNull(referenceRepository);
        _storage = Check.NotNull(storage);
        _optionsMonitor = Check.NotNull(optionsMonitor);
        _accessAuthorizer = Check.NotNull(accessAuthorizer);
        _publicFieldResolver = Check.NotNull(publicFieldResolver);
        _urlSigner = Check.NotNull(urlSigner);
        // 两道闸门（体积/扩展名 + 净化管线）收在 UploadGuard 里，由容器给同一份，
        // 三条写路径共用，避免各抄一遍后逐条漂移。
        _guard = Check.NotNull(guard);
        _thumbnails = Check.NotNull(thumbnails);
    }

    /// <summary>
    /// 上传时要不要为这个扩展名自动画缩略图：总开关开着，且生成器此刻画得出这种文件。
    /// </summary>
    private bool ShouldAutoGenerateThumbnail(string? extension)
        => Options.AutoGenerateThumbnail && _thumbnails.CanGenerate(extension);

    public async Task<Result<FileRecord>> SaveAsync(string originalFileName, Stream stream, bool isTemporary = false, bool isPublic = false)
    {
        var uploaded = new List<string>();
        try
        {
            return await SaveCoreAsync(originalFileName, stream, isTemporary, isPublic, uploaded);
        }
        catch
        {
            // 对象已经交给 provider、记录却没落成（InsertAsync / 缩略图抛了）：不收拾就是一个
            // 任何清理都看不见的孤儿对象 —— 孤儿回收按 FileRecord 枚举，而这里根本没有记录。
            await DiscardUploadedObjectsAsync(uploaded);
            throw;
        }
    }

    /// <summary>
    /// 单文件保存的主体。<paramref name="uploadedObjects"/> 收集本次调用交给 provider 的每一个对象键
    /// （正文、缩略图、按原键重传），供调用方在中途失败时删掉 —— <see cref="SaveManyAsync"/> 作废整批时
    /// 靠它知道哪些对象是这一批写进去的。
    /// </summary>
    private async Task<Result<FileRecord>> SaveCoreAsync(
        string originalFileName, Stream stream, bool isTemporary, bool isPublic, List<string> uploadedObjects)
    {
        var validation = ValidateFileName<FileRecord>(originalFileName);
        if (validation != null)
            return validation;
        validation = ValidateStream<FileRecord>(stream);
        if (validation != null)
            return validation;

        // 文件验证
        var fileValidation = ValidateFile<FileRecord>(originalFileName, stream);
        if (fileValidation != null)
            return fileValidation;

        var extension = Path.GetExtension(originalFileName);
        var contentType = FileTypeHelper.GetContentType(extension);

        // 净化管线必须跑在 MD5 之前：净化器可以改写内容（剥元数据、重编码），
        // 而 MD5 既用于去重也用于完整性校验——基于原始内容算出的哈希
        // 与实际落库的字节对不上，会让去重命中错误的记录、让完整性校验永远失败。
        // 未注册任何净化器时 Passthrough，不读流也不产生开销。
        await using var sanitized = await _guard.RunAsync(originalFileName, extension, contentType, stream);
        if (sanitized.IsRejected)
        {
            return Fail<FileRecord>(sanitized.Reason!, 400);
        }

        stream = sanitized.Content;

        // 计算 MD5 + 按 MD5 去重（受 EnableMd5Validation 控制；关闭时跳过两者，每次都产生独立记录）
        string? md5Hash = null;
        if (Options.EnableMd5Validation)
        {
            md5Hash = await HashHelper.GetMd5Async(stream);
            stream.Position = 0;

            // 检查是否已存在相同MD5的文件
            var existingResult = await TryGetExistingFileByMd5Async(md5Hash, originalFileName, stream, isTemporary, isPublic, uploadedObjects);
            if (existingResult != null)
            {
                return existingResult;
            }
        }

        // 存储键一律服务端生成；调用方给的名字只进 OriginalName（见 StorageKeyHelper）。
        var fileName = StorageKeyHelper.NewKey(extension);

        // 长度必须在把流交给 provider **之前**取。流的生命周期归调用方，但 provider 读完
        // 之后这个流还能不能读，不在本服务的控制之内（见 IFileStorage.UploadAsync 的所有权约定）；
        // 上传后再读 Length 会被已关闭的流直接打成 ObjectDisposedException。
        var knownSize = TryGetStreamLength(stream);

        // 1. 上传文件到存储
        var filePath = await _storage.UploadAsync(fileName, stream, contentType);
        uploadedObjects.Add(filePath);
        var size = await ResolveStoredSizeAsync(knownSize, filePath);

        // 2. 位图与 PDF（后者需可选包 Tnzi.Documents）生成缩略图；画不出来只是没有图，不影响保存
        string? thumbnailPath = null;
        if (ShouldAutoGenerateThumbnail(extension))
        {
            thumbnailPath = await _thumbnails.GenerateAsync(filePath, fileName, extension, size);
            if (thumbnailPath != null)
                uploadedObjects.Add(thumbnailPath);
        }

        // 3. 保存数据库记录
        // 临时文件的 ReferenceCount 初始为 0，正式文件为 1
        var fileRecord = new FileRecord
        {
            FileName = fileName,
            OriginalName = originalFileName,
            Extension = extension,
            Size = size,
            Path = filePath,
            Md5Hash = md5Hash,
            Provider = _storage.ProviderName,
            ContentType = contentType,
            ThumbnailPath = thumbnailPath,
            IsTemporary = isTemporary,
            IsPublic = isPublic,
            ReferenceCount = isTemporary ? 0 : 1
        };

        await _repository.InsertAsync(fileRecord);
        LogInformation("File saved: {FileName}, OriginalName: {OriginalName}, Size: {Size}", fileName, originalFileName, size);

        await PublishFileUploadedEventAsync(fileRecord);

        return Ok(fileRecord, "File saved successfully");
    }

    public Task<Result<Stream>> GetAsync(Guid id)
        => GetContentAsync(id, FileAccessType.Download);

    public Task<Result<Stream>> GetForPreviewAsync(Guid id)
        => GetContentAsync(id, FileAccessType.Preview);

    /// <summary>
    /// 下载与预览共用的取流路径：授权、取字节、发一条带访问类型的 <see cref="FileAccessedEvent"/>。
    /// 访问类型只在这里被写入 —— 两条预览路由此前一条记成 Download、一条什么都不发，
    /// 正是因为各自另起了取流的路子。
    /// </summary>
    private async Task<Result<Stream>> GetContentAsync(Guid id, FileAccessType accessType)
    {
        var record = await _repository.GetAsync(id);
        var check = await EnsureReadableAsync<Stream>(record);
        if (check != null)
            return check;

        if (string.IsNullOrEmpty(record!.Path))
            return Fail<Stream>("File path is empty", 404, ErrorCodes.RESOURCE_NOT_FOUND);

        var stream = await _storage.DownloadAsync(GetSafePath(record!.Path));
        await PublishFileAccessedEventAsync(id, accessType);
        return Ok(stream);
    }

    public async Task<Result<(Stream Stream, long Start, long End, long TotalLength)>> GetRangeAsync(
        Guid id,
        long? rangeStart = null,
        long? rangeEnd = null)
    {
        var record = await _repository.GetAsync(id);
        var check = await EnsureReadableAsync<(Stream Stream, long Start, long End, long TotalLength)>(record);
        if (check != null)
            return check;

        if (string.IsNullOrEmpty(record!.Path))
            return Fail<(Stream Stream, long Start, long End, long TotalLength)>("File path is empty", 404, ErrorCodes.RESOURCE_NOT_FOUND);

        var result = await _storage.DownloadRangeAsync(GetSafePath(record!.Path), rangeStart, rangeEnd);
        await PublishFileAccessedEventAsync(id, FileAccessType.RangeDownload);
        return Ok(result);
    }

    public async Task<Result<FileRecord>> GetRecordAsync(Guid id)
    {
        var record = await _repository.GetAsync(id);
        var check = await EnsureReadableAsync<FileRecord>(record);
        if (check != null)
            return check;
        return Ok(record!);
    }

    public async Task<Result<FileInfoDto>> GetFileInfoAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var record = await _repository.GetAsync(id, cancellationToken);
        var check = await EnsureReadableAsync<FileInfoDto>(record, cancellationToken);
        if (check != null)
            return check;

        var dto = record!.MapTo<FileInfoDto>();
        return Ok(dto);
    }

    public async Task<Result<string>> GetUrlAsync(Guid id, int? expiresIn = null)
    {
        var record = await _repository.GetAsync(id);
        var check = await EnsureReadableAsync<string>(record);
        if (check != null)
            return check;

        // 返回通过控制器访问的 URL（安全访问，不直接暴露文件路径）
        var url = $"/api/files/{id}/download";
        return Ok<string>(url);
    }

    public async Task<Result<Stream>> GetThumbnailAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var record = await _repository.GetAsync(id, cancellationToken);
        var check = await EnsureReadableAsync<Stream>(record, cancellationToken);
        if (check != null)
            return check;

        if (string.IsNullOrEmpty(record!.ThumbnailPath))
            return Fail<Stream>("Thumbnail not available", 404, ErrorCodes.RESOURCE_NOT_FOUND);

        var stream = await _storage.DownloadAsync(record.ThumbnailPath);
        await PublishFileAccessedEventAsync(id, FileAccessType.Thumbnail);
        return Ok(stream);
    }

    public async Task<Result> DeleteAsync(Guid id)
    {
        var record = await _repository.GetAsync(id);
        var check = await EnsureWritableAsync<object>(record);
        if (check != null)
            return check;

        // 减少引用计数
        var newCount = DecrementReferenceCount(record!);
        if (newCount > 0)
        {
            // 还有引用，只更新计数
            await _repository.UpdateAsync(record!);
            LogInformation("File reference count decreased: {FileName}, ReferenceCount: {ReferenceCount}", record!.FileName, newCount);
            return Ok("File reference count decreased");
        }

        // 1. 批量删除所有引用记录
        await _referenceRepository.DeleteAsync(r => r.FileId == id);

        // 2. 删除物理文件，仅当物理删除成功（或文件已不存在）才删数据库记录
        var dbDeleted = await DeleteFileAsync(record!);
        if (!dbDeleted)
        {
            // 物理删除失败，DB 记录保留（ReferenceCount 已为 0），交后台清理任务重试
            LogInformation("File reference count zeroed but physical delete failed, deferred to background cleanup: {FileName}", record!.FileName);
            return Ok("File reference count zeroed; physical file deletion deferred to background cleanup");
        }

        LogInformation("File deleted: {FileName}, OriginalName: {OriginalName}", record!.FileName, record!.OriginalName);
        return Ok("File deleted successfully");
    }

    public async Task<Result<FileRecord>> GetOrCreateByMd5Async(string md5Hash, string fileName, Stream stream)
    {
        // 入参校验必须先于去重查询：命中"记录存在但物理文件缺失"分支时会回读 stream 重传，
        // 若此时 stream/fileName 非法会抛 NullReferenceException 而不是返回 400。
        var validation = ValidateFileName<FileRecord>(fileName);
        if (validation != null)
            return validation;
        validation = ValidateStream<FileRecord>(stream);
        if (validation != null)
            return validation;

        validation = ValidateFile<FileRecord>(fileName, stream);
        if (validation != null)
            return validation;

        var extension = Path.GetExtension(fileName);
        var contentType = FileTypeHelper.GetContentType(extension);

        // 与 SaveAsync 同一道闸门、同一个位置（去重与哈希之前）：这是 IFileStorageService 上的公开写路径，
        // 消费方经它直存字节时同样把内容交给了 provider。
        await using var sanitized = await _guard.RunAsync(fileName, extension, contentType, stream);
        if (sanitized.IsRejected)
        {
            return Fail<FileRecord>(sanitized.Reason!, 400);
        }

        if (!ReferenceEquals(sanitized.Content, stream))
        {
            // 净化器改写了内容：调用方给的哈希描述的是一份永远不会落库的字节。去重与落库都按净化后的
            // 哈希走，而不是当成「哈希不符」拒掉 —— 后者会让每一个剥元数据的部署都存不进图片。
            stream = sanitized.Content;
            md5Hash = await HashHelper.GetMd5Async(stream);
            stream.Position = 0;
        }

        // 检查是否已存在相同MD5的文件
        // 这条路径建的是正式记录（ReferenceCount 从 1 起），复用时也按正式持有者对待。
        var existingResult = await TryGetExistingFileByMd5Async(md5Hash, fileName, stream, isTemporary: false);
        if (existingResult != null)
        {
            return existingResult;
        }

        // 验证 MD5
        var calculatedMd5 = await HashHelper.GetMd5Async(stream);
        stream.Position = 0;
        if (!string.IsNullOrEmpty(md5Hash) && calculatedMd5 != md5Hash)
        {
            return Fail<FileRecord>("File MD5 hash mismatch", 400, ErrorCodes.VALIDATION_ERROR);
        }

        var newFileName = StorageKeyHelper.NewKey(extension);

        // 与 SaveAsync 同理：长度在交给 provider 之前取。
        var knownSize = TryGetStreamLength(stream);
        var filePath = await _storage.UploadAsync(newFileName, stream, contentType);
        var size = await ResolveStoredSizeAsync(knownSize, filePath);

        string? thumbnailPath = null;
        if (ShouldAutoGenerateThumbnail(extension))
        {
            thumbnailPath = await _thumbnails.GenerateAsync(filePath, newFileName, extension, size);
        }

        var fileRecord = new FileRecord
        {
            FileName = newFileName,
            OriginalName = fileName,
            Extension = extension,
            Size = size,
            Path = filePath,
            Md5Hash = calculatedMd5,
            Provider = _storage.ProviderName,
            ContentType = contentType,
            ThumbnailPath = thumbnailPath,
            ReferenceCount = 1
        };

        await _repository.InsertAsync(fileRecord);
        LogInformation("File saved: {FileName}, OriginalName: {OriginalName}, Size: {Size}", newFileName, fileName, size);
        return Ok(fileRecord, "File saved successfully");
    }

    /// <summary>
    /// 批量保存：一次意图，要么整批落库，要么一条记录、一个对象都不留。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ 此前它在工作单元里逐个 <c>SaveAsync</c>，第一条失败就 <b>return Fail</b> —— 而
    /// <see cref="ApplicationService.ExecuteInUnitOfWorkAsync{TResult}"/> 只在<b>异常</b>时回滚，正常返回的失败结果
    /// 照样提交：<c>[a.png, b.png, evil.exe]</c> 回 400，前两条却以 <c>ReferenceCount = 1</c> 的正式记录留了下来 ——
    /// 孤儿回收永远不收它们，错误响应里也没有它们的 id，重试整批又经 MD5 去重把计数再加一。
    /// 镜像形态：provider 在第三条上抛异常，行回滚了，前两条的字节却留在桶里、没有任何记录指向。
    /// </para>
    /// <para>
    /// 现在分三层：①便宜的闸门（文件名 / 流 / 体积 / 扩展名）先整批过一遍，一条不合规整批拒绝，此时一个字节都没写；
    /// ②逐条保存跑在工作单元里，中途失败以异常离开让工作单元回滚，再把失败结果翻译回 <c>Result</c>；
    /// ③本批交给 provider 的对象逐个删掉（去重命中的既有记录**不在其列**：它的对象不是这批写的，计数由回滚退回）。
    /// 行的原子性依赖工作单元（<c>EFCoreModule</c> 必定注册它，本模块 <c>[DependsOn]</c> 它）；
    /// 没有工作单元管理器时只能收拾对象并记一条警告。
    /// </para>
    /// </remarks>
    public async Task<Result<IEnumerable<FileRecord>>> SaveManyAsync(IEnumerable<(string fileName, Stream stream)> files, bool isPublic = false)
    {
        Check.NotNull(files);
        var batch = files.ToList();

        foreach (var (fileName, stream) in batch)
        {
            var validation = ValidateFileName<IEnumerable<FileRecord>>(fileName)
                ?? ValidateStream<IEnumerable<FileRecord>>(stream)
                ?? ValidateFile<IEnumerable<FileRecord>>(fileName, stream);
            if (validation != null)
                return validation;
        }

        var uploaded = new List<string>();
        var saved = new List<FileRecord>();
        try
        {
            return await ExecuteInUnitOfWorkAsync(async cancellationToken =>
            {
                foreach (var (fileName, stream) in batch)
                {
                    var result = await SaveCoreAsync(fileName, stream, isTemporary: false, isPublic, uploaded);
                    if (!result.Succeeded)
                    {
                        // 以异常离开工作单元才会回滚；失败结果在下面的 catch 里翻译回 Result。
                        throw new BatchSaveAbortedException(result);
                    }
                    saved.Add(result.Data!);
                }
                LogInformation("Batch saved {Count} files", saved.Count);
                return Ok((IEnumerable<FileRecord>)saved.ToList(), $"Batch saved {saved.Count} files");
            });
        }
        catch (BatchSaveAbortedException ex)
        {
            await AbandonBatchAsync(saved, uploaded);
            return Fail<IEnumerable<FileRecord>>(ex.Failure.Message ?? "Failed to save file", ex.Failure.Code ?? 500, ex.Failure.ErrorCode);
        }
        catch
        {
            await AbandonBatchAsync(saved, uploaded);
            throw;
        }
    }

    /// <summary>
    /// 作废一批保存：把回滚前挂在变更跟踪器上的实体摘掉（否则同一作用域里下一次 SaveChanges 会把它们原样重放，
    /// 同 <see cref="IRepository{TEntity}.Discard"/> 的理由），再删掉本批交给 provider 的对象。
    /// </summary>
    /// <remarks>
    /// 没有 <c>IUnitOfWorkManager</c> 时 <c>ExecuteInUnitOfWorkAsync</c> 直接执行委托、仓储逐条立即提交：
    /// 失败之前的记录**已经在库里**，摘不掉也回滚不了。那些记录的对象必须留下 —— 删掉等于把「孤儿但自洽的记录」
    /// 变成「记录在、字节没了」（下载 500），比原来更糟；只删没落成记录的那一条交给 provider 的对象。
    /// </remarks>
    private async Task AbandonBatchAsync(List<FileRecord> saved, List<string> uploaded)
    {
        foreach (var record in saved)
        {
            _repository.Discard(record);
        }

        if (UnitOfWorkManager == null && saved.Count > 0)
        {
            LogWarning(
                "SaveManyAsync aborted without a unit of work manager: {Count} record(s) saved before the failure were already committed and are kept together with their objects.",
                saved.Count);

            var committedObjects = saved
                .SelectMany(r => new[] { r.Path, r.ThumbnailPath })
                .Where(p => !string.IsNullOrEmpty(p))
                .ToHashSet(StringComparer.Ordinal);
            uploaded.RemoveAll(committedObjects.Contains);
        }

        await DiscardUploadedObjectsAsync(uploaded);
    }

    /// <summary>
    /// 删掉一批已交给 provider、却不会有记录指向的对象。删除失败只记日志：这里已经在收拾一次失败了。
    /// </summary>
    private async Task DiscardUploadedObjectsAsync(List<string> uploaded)
    {
        foreach (var path in uploaded)
        {
            try
            {
                await _storage.DeleteAsync(GetSafePath(path));
            }
            catch (Exception ex)
            {
                LogWarning("Failed to discard uploaded object {Path} after an aborted save: {Error}", path, ex.Message);
            }
        }

        uploaded.Clear();
    }

    /// <summary>
    /// 批量保存中途某一条以失败结果返回：用异常把它带出工作单元（只有异常才回滚），在外层翻译回 <c>Result</c>。
    /// 不是给调用方看的，永远在 <see cref="SaveManyAsync"/> 内部被接住。
    /// </summary>
    private sealed class BatchSaveAbortedException(Result<FileRecord> failure) : Exception(failure.Message)
    {
        public Result<FileRecord> Failure { get; } = failure;
    }

    public async Task<Result> DeleteManyAsync(IEnumerable<Guid> ids)
    {
        return await ExecuteInUnitOfWorkAsync(async cancellationToken =>
        {
            var idList = ids.ToList();
            var records = await _repository
                .ToListAsync(r => idList.Contains(r.Id), cancellationToken);

            var deletedCount = 0;
            var decrementedCount = 0;

            foreach (var record in records)
            {
                var newCount = DecrementReferenceCount(record);
                if (newCount > 0)
                {
                    // 还有引用，只更新计数
                    await _repository.UpdateAsync(record, cancellationToken);
                    decrementedCount++;
                    continue;
                }

                // 引用归零，批量删除引用记录和物理文件
                await _referenceRepository.DeleteAsync(r => r.FileId == record.Id, cancellationToken);

                // 仅当物理删除成功（或文件已不存在）才计为已删除；失败则 DB 记录保留交后台清理
                if (await DeleteFileAsync(record))
                {
                    deletedCount++;
                }
            }

            LogInformation("Batch delete: {Deleted} deleted, {Decremented} reference count decreased", deletedCount, decrementedCount);
            return Ok($"Batch delete: {deletedCount} deleted, {decrementedCount} reference count decreased");
        });
    }

    public async Task<Result<FileRecord>> RenameAsync(Guid id, string newFileName)
    {
        var record = await _repository.GetAsync(id);
        var check = await EnsureWritableAsync<FileRecord>(record);
        if (check != null)
            return check;

        var oldName = record!.OriginalName;
        record.OriginalName = newFileName;
        await _repository.UpdateAsync(record);
        LogInformation("File renamed: {OldFileName} -> {NewFileName}", oldName, newFileName);
        return Ok(record, "File renamed successfully");
    }

    public async Task<Result<FileRecord>> CopyAsync(Guid sourceFileId, string? newFileName = null, CancellationToken cancellationToken = default)
    {
        var sourceFile = await _repository.GetAsync(sourceFileId, cancellationToken);
        // Copying hands the caller the bytes under a new id, so it needs read
        // rights on the source, not merely knowledge of its id.
        var sourceCheck = await EnsureReadableAsync<FileRecord>(sourceFile, cancellationToken);
        if (sourceCheck != null)
            return sourceCheck;

        if (string.IsNullOrEmpty(sourceFile!.Path))
            return Fail<FileRecord>("Source file path is empty", 400, ErrorCodes.FILE_OPERATION_ERROR);

        var extension = sourceFile.Extension;
        var copyFileName = StorageKeyHelper.NewKey(extension);

        // 与 SaveAsync 同一形状：副本（与它的缩略图）已交给 provider、记录却没落成时，删掉它们 ——
        // 孤儿回收按 FileRecord 枚举，看不见没有记录的对象。原件不在这份清单里。
        var uploaded = new List<string>();
        try
        {
            // Prefer provider-native server-side copy (S3/R2/Azure) to avoid streaming large files
            // through the application; fall back to download + upload when unsupported (Local/InMemory).
            var newFilePath = await _storage.CopyAsync(GetSafePath(sourceFile.Path), copyFileName);
            if (string.IsNullOrEmpty(newFilePath))
            {
                using var sourceStream = await _storage.DownloadAsync(GetSafePath(sourceFile.Path));
                newFilePath = await _storage.UploadAsync(copyFileName, sourceStream, sourceFile.ContentType);
            }
            uploaded.Add(newFilePath);

            string? thumbnailPath = null;
            if (ShouldAutoGenerateThumbnail(extension))
            {
                thumbnailPath = await _thumbnails.GenerateAsync(newFilePath, copyFileName, extension, sourceFile.Size, cancellationToken);
                if (thumbnailPath != null)
                    uploaded.Add(thumbnailPath);
            }

            var newFileRecord = new FileRecord
            {
                FileName = copyFileName,
                OriginalName = newFileName ?? sourceFile.OriginalName,
                Extension = extension,
                Size = sourceFile.Size,
                Path = newFilePath,
                Md5Hash = sourceFile.Md5Hash, // 复制文件内容相同，直接复用 MD5
                Provider = sourceFile.Provider,
                ContentType = sourceFile.ContentType,
                ThumbnailPath = thumbnailPath,
                ReferenceCount = 0
            };

            await _repository.InsertAsync(newFileRecord, cancellationToken);

            LogInformation("File copied: {SourceFileId} -> {NewFileId}, FileName: {FileName}", sourceFileId, newFileRecord.Id, newFileName ?? sourceFile.OriginalName);
            return Ok(newFileRecord, "File copied successfully");
        }
        catch
        {
            await DiscardUploadedObjectsAsync(uploaded);
            throw;
        }
    }

    public async Task<Result<FileStorageStatistics>> GetStatisticsAsync()
    {
        var totalFiles = await _repository.CountAsync();
        var totalSize = await _repository.AsQueryable().SumAsync(r => (long?)r.Size) ?? 0;

        var filesByType = await _repository.AsQueryable()
            .GroupBy(r => r.Extension)
            .Select(g => new { Extension = g.Key, Count = g.Count(), Size = g.Sum(r => r.Size) })
            .ToListAsync();

        var filesByTypeDict = filesByType.ToDictionary(
            x => x.Extension ?? "unknown",
            x => new FileTypeStatistics { Count = x.Count, Size = x.Size });

        var statistics = new FileStorageStatistics
        {
            TotalFiles = totalFiles,
            TotalSize = totalSize,
            FilesByType = filesByTypeDict
        };
        return Ok(statistics);
    }

    public async Task<Result<FileRecord>> SaveWithReferenceAsync(string fileName, Stream stream, string entityType, Guid entityId, string fieldName, bool isTemporary = false, bool isPublic = false)
    {
        // 保存文件
        var saveResult = await SaveAsync(fileName, stream, isTemporary, isPublic);
        if (!saveResult.Succeeded)
        {
            return saveResult;
        }
        var fileRecord = saveResult.Data!;

        // 创建引用
        await CreateReferenceAsync(fileRecord.Id, entityType, entityId, fieldName, isTemporary);

        LogInformation("File saved with reference: {FileName}, EntityType: {EntityType}, EntityId: {EntityId}, FieldName: {FieldName}", fileName, entityType, entityId, fieldName);
        return Ok(fileRecord, "File saved with reference");
    }

    public async Task<Result<FileRecord>> SaveFromBytesAsync(string fileName, byte[] content, string? contentType = null, bool isPublic = false)
    {
        var validation = ValidateFileName<FileRecord>(fileName);
        if (validation != null)
            return validation;
        if (content == null || content.Length == 0)
            return Fail<FileRecord>("Content cannot be null or empty", 400, ErrorCodes.VALIDATION_ERROR);

        using var stream = new MemoryStream(content);
        return await SaveAsync(fileName, stream, isTemporary: false, isPublic: isPublic);
    }

    public async Task<Result<FileRecord>> SaveFromPathAsync(string filePath, string? contentType = null)
    {
        if (string.IsNullOrEmpty(filePath))
            return Fail<FileRecord>("FilePath cannot be null or empty", 400, ErrorCodes.VALIDATION_ERROR);
        if (!File.Exists(filePath))
            return Fail<FileRecord>($"File not found: {filePath}", 404, ErrorCodes.RESOURCE_NOT_FOUND);

        var fileName = Path.GetFileName(filePath);
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        return await SaveAsync(fileName, stream);
    }

    public async Task<Result<IPagedList<FileRecord>>> QueryFilesAsync(FileQueryRequest request, CancellationToken cancellationToken = default)
    {
        var query = _repository.AsQueryable();

        if (!string.IsNullOrEmpty(request.Extension))
        {
            var ext = request.Extension.ToLower();
            query = query.Where(f => f.Extension != null && f.Extension.ToLower() == ext);
        }

        if (request.MinSize.HasValue)
        {
            query = query.Where(f => f.Size >= request.MinSize.Value);
        }

        if (request.MaxSize.HasValue)
        {
            query = query.Where(f => f.Size <= request.MaxSize.Value);
        }

        if (request.StartTime.HasValue)
        {
            query = query.Where(f => f.CreationTime >= request.StartTime.Value);
        }

        if (request.EndTime.HasValue)
        {
            query = query.Where(f => f.CreationTime <= request.EndTime.Value);
        }

        if (request.CreatorId.HasValue)
        {
            query = query.Where(f => f.CreatorId == request.CreatorId.Value);
        }

        if (!string.IsNullOrEmpty(request.Provider))
        {
            var provider = request.Provider.ToLower();
            query = query.Where(f => f.Provider != null && f.Provider.ToLower() == provider);
        }

        if (!string.IsNullOrEmpty(request.ContentType))
        {
            // Prefix match so "image/" selects all images, "application/pdf" selects PDFs, etc.
            var contentType = request.ContentType;
            query = query.Where(f => f.ContentType != null && f.ContentType.StartsWith(contentType));
        }

        if (!string.IsNullOrEmpty(request.OriginalName))
        {
            var keyword = request.OriginalName.ToLower();
            query = query.Where(f => f.OriginalName != null && f.OriginalName.ToLower().Contains(keyword));
        }

        // Folder filter - two modes:
        //   1. FolderId set + IncludeUnfiled=false → that folder's direct children only.
        //   2. FolderId=null + IncludeUnfiled=true  → root/unfiled (FolderId IS NULL).
        // Other combinations leave the query unconstrained on folder so legacy
        // callers (no folder filter at all) keep working unchanged.
        if (request.IncludeUnfiled)
        {
            query = query.Where(f => f.FolderId == null);
        }
        else if (request.FolderId.HasValue)
        {
            var folderId = request.FolderId.Value;
            query = query.Where(f => f.FolderId == folderId);
        }

        if (!string.IsNullOrEmpty(request.Tag))
        {
            var tag = request.Tag.Trim().ToLower();
            query = query.Where(f => f.Tags != null && f.Tags.ToLower().Contains(tag));
        }

        // Metadata filtering: search within the JSON-serialized metadata column
        // Use JsonSerializer.Serialize to properly escape key/value and prevent JSON injection
        if (!string.IsNullOrEmpty(request.MetadataKey))
        {
            var escapedKey = JsonSerializer.Serialize(request.MetadataKey.Trim());
            if (!string.IsNullOrEmpty(request.MetadataValue))
            {
                // Exact key-value match: search for "key":"value" pattern in JSON
                var escapedValue = JsonSerializer.Serialize(request.MetadataValue.Trim());
                var searchPattern = $"{escapedKey}:{escapedValue}";
                query = query.Where(f => f.Metadata != null && f.Metadata.ToLower().Contains(searchPattern.ToLower()));
            }
            else
            {
                // Key existence: search for "key": pattern in JSON
                var searchPattern = $"{escapedKey}:";
                query = query.Where(f => f.Metadata != null && f.Metadata.ToLower().Contains(searchPattern.ToLower()));
            }
        }

        // Resolve sort direction: SortOrder string ("asc"/"desc") takes precedence over the
        // legacy bool Descending; when SortOrder is unset, fall back to Descending.
        var descending = !string.IsNullOrEmpty(request.SortOrder)
            ? request.SortOrder.Equals("desc", StringComparison.OrdinalIgnoreCase)
            : request.Descending;

        if (!string.IsNullOrEmpty(request.SortBy))
        {
            query = request.SortBy.ToLowerInvariant() switch
            {
                "creationtime" => descending
                    ? query.OrderByDescending(f => f.CreationTime)
                    : query.OrderBy(f => f.CreationTime),
                "size" => descending
                    ? query.OrderByDescending(f => f.Size)
                    : query.OrderBy(f => f.Size),
                "originalname" => descending
                    ? query.OrderByDescending(f => f.OriginalName)
                    : query.OrderBy(f => f.OriginalName),
                _ => query.OrderByDescending(f => f.CreationTime)
            };
        }
        else
        {
            query = query.OrderByDescending(f => f.CreationTime);
        }

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .Skip((request.PageIndex - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        IPagedList<FileRecord> pagedList = new PagedList<FileRecord>(items, request.PageIndex, request.PageSize, total);
        return Ok(pagedList);
    }

    public async Task<Result<FileRecord>> CompressAsync(IEnumerable<Guid> fileIds, string? zipFileName = null, CancellationToken cancellationToken = default)
    {
        var validation = ValidateFileIds<FileRecord>(fileIds);
        if (validation != null)
            return validation;
        var fileIdList = fileIds!.ToList();

        // 一次打包的条目数有上限：几万个 id 的一次请求会把临时目录与对象存储一起打满，
        // 而调用方只需要登录（见 ArchiveOptions）。
        var maxEntries = Options.Archive.MaxEntries;
        if (fileIdList.Count > maxEntries)
        {
            return Fail<FileRecord>(
                $"Too many files to compress: {fileIdList.Count} (maximum {maxEntries}).",
                400, ErrorCodes.VALIDATION_ERROR);
        }

        var tempFilePath = Path.GetTempFileName();
        try
        {
            // 使用临时文件而非 MemoryStream，避免大文件占用大量内存
            using (var zipFileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.ReadWrite, System.IO.FileShare.None))
            {
                using (var archive = new ZipArchive(zipFileStream, ZipArchiveMode.Create, true))
                {
                    foreach (var fileId in fileIdList)
                    {
                        var fileRecord = await _repository.GetAsync(fileId, cancellationToken);
                        if (fileRecord == null)
                            continue;

                        // ★ 打包等于把字节交到调用者手上，与 CopyAsync 是同一件事，
                        // 因此需要的是**读权限**而不只是知道 id。少了这一句，任意已登录用户
                        // 拿到他人 fileId 即可打包下载：新 zip 的 CreatorId 是调用者自己，
                        // 随后走 IsOwner 分支正常下载，7 条判据链被整体绕过。
                        //
                        // 越权 id 静默省略而不是让整批失败，与 CreateAccessTokensAsync 同一取舍：
                        // 省略本身不透露那个 id 上是否真有文件（不可读与不存在观测上不可区分）。
                        if (await EnsureReadableAsync<FileRecord>(fileRecord, cancellationToken) != null)
                            continue;

                        using var fileStream = await _storage.DownloadAsync(GetSafePath(fileRecord.Path));
                        var entry = archive.CreateEntry(fileRecord.OriginalName ?? fileRecord.FileName);
                        using (var entryStream = entry.Open())
                        {
                            await fileStream.CopyToAsync(entryStream, cancellationToken);
                        }
                    }
                }

                // MD5 与大小都在交给 provider **之前**算完：上传之后这个流是否还可读
                // 由 provider 决定（见 IFileStorage.UploadAsync 的所有权约定）。
                zipFileStream.Position = 0;
                var md5Hash = await HashHelper.GetMd5Async(zipFileStream);
                var zipSize = zipFileStream.Length;

                zipFileStream.Position = 0;

                // ★ 存储键由服务端生成，调用方给的 zipFileName 只做展示名。它来自请求体：
                //   直接拿它当键等于让任何已登录用户选定对象名，在对象存储上（键即对象名、
                //   PutObject 默认覆盖）这就是「知道别人文件的名字 → 替换掉它的字节」，
                //   CanWriteAsync 整条判据链被绕过（见 StorageKeyHelper）。
                var zipKey = StorageKeyHelper.NewKey(".zip");
                var zipName = ResolveArchiveDisplayName(zipFileName);
                var zipPath = await _storage.UploadAsync(zipKey, zipFileStream, "application/zip");

                var zipRecord = new FileRecord
                {
                    FileName = zipKey,
                    OriginalName = zipName,
                    Extension = ".zip",
                    Size = zipSize,
                    Path = zipPath,
                    Md5Hash = md5Hash,
                    Provider = _storage.ProviderName,
                    ContentType = "application/zip",
                    ReferenceCount = 0
                };

                try
                {
                    await _repository.InsertAsync(zipRecord, cancellationToken);
                }
                catch
                {
                    // 压缩包已交给 provider、记录没落成：与 SaveAsync 同一形状，删掉它，
                    // 否则是一个孤儿回收看不见的对象（它按 FileRecord 枚举）。
                    await DiscardUploadedObjectsAsync([zipPath]);
                    throw;
                }

                LogInformation("Files compressed: {Count} files -> {ZipFileName}", fileIdList.Count, zipName);
                return Ok(zipRecord, $"Compressed {fileIdList.Count} files successfully");
            }
        }
        finally
        {
            if (File.Exists(tempFilePath))
                File.Delete(tempFilePath);
        }
    }

    public async Task<Result<IEnumerable<FileRecord>>> DecompressAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        var fileRecord = await _repository.GetAsync(fileId, cancellationToken);
        // ★ 解包把整个压缩包的内容交到调用者手上（解出的每条记录 CreatorId 都是他），
        // 所以要的是对这个 zip 的读权限，不是知道它的 id。与其它单文件操作同形：
        // 不可读与不存在都返回 404，不泄露存在性。
        var readCheck = await EnsureReadableAsync<IEnumerable<FileRecord>>(fileRecord, cancellationToken);
        if (readCheck != null)
            return readCheck;

        if (fileRecord!.Extension != ".zip")
            return Fail<IEnumerable<FileRecord>>("Only ZIP files can be decompressed", 400, ErrorCodes.FILE_OPERATION_ERROR);

        var extractedFiles = new List<FileRecord>();
        var archiveOptions = Options.Archive;
        var maxEntrySize = Options.MaxFileSize;
        long extractedTotal = 0;

        using var zipStream = await _storage.DownloadAsync(GetSafePath(fileRecord.Path));
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Read))
        {
            // 条目数上限先判：目录项（Name 为空）不算。
            var entries = archive.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();
            if (entries.Count > archiveOptions.MaxEntries)
            {
                return Fail<IEnumerable<FileRecord>>(
                    $"The archive has too many entries: {entries.Count} (maximum {archiveOptions.MaxEntries}).",
                    400, ErrorCodes.VALIDATION_ERROR);
            }

            foreach (var entry in entries)
            {
                // 解包等于把 zip 里的每一条都写成一条独立的文件记录，所以它是一条真正的
                // 写入路径，必须过和直传同一道闸门 —— 否则把 payload.exe 塞进一个 .zip
                // 就绕开了扩展名白名单。★ 单条不合规只跳过它，不作废整包：一个压缩包里
                // 混进一个不允许的类型，不该让其余几十条合规内容也解不出来。
                if (_guard.ValidateExtension<FileRecord>(entry.Name) != null)
                {
                    LogWarning(
                        "Skipped zip entry {EntryName} from {FileId}: file type is not allowed.",
                        entry.Name, fileId);
                    continue;
                }

                // 头里声称的大小先拦一次（便宜）；真正的判据是下面复制时数出来的字节 —— 头可以撒谎。
                if (entry.Length > maxEntrySize)
                {
                    LogWarning(
                        "Skipped zip entry {EntryName} from {FileId}: declared size {Size} exceeds MaxFileSize {Limit}.",
                        entry.Name, fileId, entry.Length, maxEntrySize);
                    continue;
                }

                // 使用临时文件而非 MemoryStream，避免大条目占用大量内存
                var tempFilePath = Path.GetTempFileName();
                try
                {
                    using (var tempStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.ReadWrite, System.IO.FileShare.None))
                    {
                        long entrySize;
                        using (var entryStream = entry.Open())
                        {
                            // 边复制边数：超过单文件上限即停，不把剩余的源读完（zip bomb 的代价就在「读完」）。
                            entrySize = await StreamLimitHelper.CopyBoundedAsync(entryStream, tempStream, maxEntrySize, cancellationToken);
                        }

                        if (entrySize < 0)
                        {
                            LogWarning(
                                "Skipped zip entry {EntryName} from {FileId}: actual size exceeds MaxFileSize {Limit}.",
                                entry.Name, fileId, maxEntrySize);
                            continue;
                        }

                        // 总体积越界：作废整次解包。已解出的对象删掉、一条记录都不落 ——
                        // 半包成功比整包失败更难被人发现。
                        extractedTotal += entrySize;
                        if (extractedTotal > archiveOptions.MaxTotalExtractedBytes)
                        {
                            await DiscardExtractedAsync(extractedFiles);
                            return Fail<IEnumerable<FileRecord>>(
                                $"The archive expands to more than the allowed total of {archiveOptions.MaxTotalExtractedBytes} bytes.",
                                400, ErrorCodes.VALIDATION_ERROR);
                        }

                        var entryExtension = Path.GetExtension(entry.Name);
                        var contentType = FileTypeHelper.GetContentType(entryExtension);

                        // 净化管线：解出来的每一条都是一次独立的写入，必须过与直传同一份闸门，位置同样在
                        // 算 MD5 与交给 provider 之前。此前这里只过扩展名白名单 —— 注册了病毒扫描器的部署，
                        // 上传一个干净的 zip 再解压，包里每一条都绕过扫描器落进对象存储。被拒的条目沿用
                        // 「跳过该条、不作废整包」的取舍。
                        tempStream.Position = 0;
                        await using var sanitized = await _guard.RunAsync(entry.Name, entryExtension, contentType, tempStream);
                        if (sanitized.IsRejected)
                        {
                            LogWarning(
                                "Skipped zip entry {EntryName} from {FileId}: rejected by the upload sanitizer ({Reason}).",
                                entry.Name, fileId, sanitized.Reason);
                            continue;
                        }

                        var content = sanitized.Content;
                        var storedSize = TryGetStreamLength(content) ?? entrySize;
                        content.Position = 0;
                        var md5Hash = await HashHelper.GetMd5Async(content);
                        content.Position = 0;

                        // ★ 键由服务端生成：条目名是上传者写进压缩包里的，同 CompressAsync 的理由。
                        //   条目名只进 OriginalName。
                        var entryKey = StorageKeyHelper.NewKey(entryExtension);
                        var extractedPath = await _storage.UploadAsync(entryKey, content, contentType);

                        extractedFiles.Add(new FileRecord
                        {
                            FileName = entryKey,
                            OriginalName = entry.Name,
                            Extension = entryExtension,
                            Size = storedSize,
                            Path = extractedPath,
                            Md5Hash = md5Hash,
                            Provider = _storage.ProviderName,
                            ContentType = contentType,
                            ReferenceCount = 0
                        });
                    }
                }
                finally
                {
                    if (File.Exists(tempFilePath))
                        File.Delete(tempFilePath);
                }
            }
        }

        if (extractedFiles.Count > 0)
            await _repository.InsertManyAsync(extractedFiles, cancellationToken);

        LogInformation("File decompressed: {FileId}, Extracted {Count} files", fileId, extractedFiles.Count);
        return Ok((IEnumerable<FileRecord>)extractedFiles, $"Decompressed {extractedFiles.Count} files");
    }

    public async Task<Result<string>> GetPresignedUrlAsync(Guid id, int expiresInSeconds = 3600, string httpMethod = "GET")
    {
        if (expiresInSeconds <= 0)
            return Fail<string>("ExpiresInSeconds must be greater than 0", 400, ErrorCodes.VALIDATION_ERROR);

        // 动词决定签出的是哪一种凭据，所以先把它钉死：未知动词不能落到「当 GET 处理」——
        // provider 对非 PUT 一律签读 URL，一个拼错的动词会安静地拿到与请求不符的东西。
        var verb = NormalizePresignedVerb(httpMethod);
        if (verb == null)
            return Fail<string>("HttpMethod must be GET or PUT", 400, ErrorCodes.VALIDATION_ERROR);

        var record = await _repository.GetAsync(id);
        // A presigned URL bypasses this API entirely once minted, so the gate has to
        // match the credential being issued: a GET URL is a read credential of the same
        // family as an access token (no Authorization header, dies only by expiry, and
        // the cloud lifetime is not even capped by SignedUrlTtlSeconds), so it needs the
        // *mint* verdict - the read verdict minus request-level credentials. Otherwise a
        // holder of a 10-minute ?sig= render token or a capped share link could trade it
        // here for a week-long cloud URL, or (local fallback) for a fresh signed link that
        // feeds back into this same endpoint forever. A PUT URL lets its holder overwrite
        // the object's bytes without the upload guard or the .update code ever running,
        // so it needs CanWrite.
        // ★ Both used to check CanRead only - any storage.file.view holder could mint
        //   a direct-upload URL for any file on S3/R2/Azure, and any request-credential
        //   holder could renew itself through the GET branch.
        var check = verb == PresignedPutVerb
            ? await EnsureWritableAsync<string>(record)
            : await EnsureMintableAsync<string>(record);
        if (check != null)
            return check;

        if (string.IsNullOrEmpty(record!.Path))
            return Fail<string>("File path is empty", 404, ErrorCodes.RESOURCE_NOT_FOUND);

        // Try provider-level presigned URL first (S3, R2, Azure)
        var presignedUrl = await _storage.GetPresignedUrlAsync(record.Path, expiresInSeconds, verb);
        if (!string.IsNullOrEmpty(presignedUrl))
        {
            LogInformation("Presigned URL generated for file {FileId}, method: {HttpMethod}, expires: {ExpiresIn}s", id, verb, expiresInSeconds);
            return Ok<string>(presignedUrl);
        }

        // 下面的回退是一条**只读**链接。把它当作 PUT 的答案交出去就是「要写凭据、拿到读凭据、
        // 状态 200」：调用方拿去上传会失败在一个与它的请求无关的地方。
        if (verb == PresignedPutVerb)
            return Fail<string>("Direct-upload presigned URLs are not supported by the current storage provider", 501);

        // 本地存储没有对象存储那套预签名。此前这里回一个裸的控制器 URL —— 对私密文件
        // 那是个**打不开的链接**(匿名请求拿不到)。改为带上签名令牌,语义与云端预签名对齐:
        // 一个到期即失效、无需 Authorization 头的读链接。
        // 同一条上限:本地回退签发的也是访问令牌,不该因为走了 presigned-url 这个入口
        // 就能要到更长的有效期(云端 provider 的过期由对象存储自己约束,不经这里)。
        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(ResolveTokenTtl(expiresInSeconds));
        var signature = _urlSigner.Sign(id, expiresAt, CurrentUser?.Id);
        var fallbackUrl = $"/api/files/{id}/download?{IFileUrlSigner.QueryParameterName}={Uri.EscapeDataString(signature)}";
        return Ok<string>(fallbackUrl, "Presigned URL not supported by provider, returning a signed controller URL");
    }

    public async Task<Result<FileAccessTokenDto>> CreateAccessTokenAsync(Guid fileId, int? expiresInSeconds = null, CancellationToken cancellationToken = default)
    {
        var ttl = ResolveTokenTtl(expiresInSeconds);
        if (ttl <= 0)
            return Fail<FileAccessTokenDto>("ExpiresInSeconds must be greater than 0", 400, ErrorCodes.VALIDATION_ERROR);

        var record = await _repository.GetAsync(fileId, cancellationToken);

        // 签发即授权:令牌绕过 Authorization 头,所以只有此刻确实读得了这个文件的人
        // 才配拿到它。读不了同样返回 404,不泄露该 id 上是否有东西。
        var check = await EnsureMintableAsync<FileAccessTokenDto>(record, cancellationToken);
        if (check != null)
            return check;

        return Ok(MintToken(fileId, ttl));
    }

    public async Task<Result<IReadOnlyList<FileAccessTokenDto>>> CreateAccessTokensAsync(
        IReadOnlyCollection<Guid> fileIds, int? expiresInSeconds = null, CancellationToken cancellationToken = default)
    {
        Check.NotNull(fileIds);

        var ttl = ResolveTokenTtl(expiresInSeconds);
        if (ttl <= 0)
            return Fail<IReadOnlyList<FileAccessTokenDto>>("ExpiresInSeconds must be greater than 0", 400, ErrorCodes.VALIDATION_ERROR);

        var ids = fileIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (ids.Count == 0)
            return Ok<IReadOnlyList<FileAccessTokenDto>>([]);

        // 上限而不是静默截断:截断会让调用方以为"这些 id 都不可读",而实际上只是没被处理。
        // 每个 id 都可能触发一次引用表查询,所以这条上限同时是放大攻击的闸门。
        if (ids.Count > MaxAccessTokenBatchSize)
        {
            return Fail<IReadOnlyList<FileAccessTokenDto>>(
                $"At most {MaxAccessTokenBatchSize} file ids can be requested at once", 400, ErrorCodes.VALIDATION_ERROR);
        }

        var records = await _repository.ToListAsync(f => ids.Contains(f.Id), cancellationToken);

        var tokens = new List<FileAccessTokenDto>(records.Count);
        foreach (var record in records)
        {
            // 越权 / 不存在的 id 静默省略而不是让整批失败:一页图片里混进一个不该看的 id
            // 时,其余图片仍应正常显示;省略本身也不透露那个 id 上是否真有文件。
            if (await _accessAuthorizer.CanMintAccessTokenAsync(record, cancellationToken))
                tokens.Add(MintToken(record.Id, ttl));
        }

        return Ok<IReadOnlyList<FileAccessTokenDto>>(tokens);
    }

    /// <summary>
    /// 一批最多能签发多少个令牌。前端一页最多要 100 个,这条上限只挡异常调用。
    /// </summary>
    private const int MaxAccessTokenBatchSize = 200;

    /// <summary>
    /// `SignedUrlTtlSeconds` 既是默认值**也是上限**:调用方只能要更短的,不能要更长的。
    /// 否则 `?expiresInSeconds=999999999` 就能把一个几分钟的凭据变成几十年的 ——
    /// 而 TTL 正是这套机制唯一的止损面(URL 会进浏览器历史、referrer 与访问日志,
    /// 且签发之后即便用户失去权限,令牌仍然有效直到过期)。
    /// </summary>
    private int ResolveTokenTtl(int? expiresInSeconds)
    {
        var ceiling = Options.SignedUrlTtlSeconds;
        return expiresInSeconds is { } requested ? Math.Min(requested, ceiling) : ceiling;
    }

    private FileAccessTokenDto MintToken(Guid fileId, int ttlSeconds)
    {
        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(ttlSeconds);
        return new FileAccessTokenDto
        {
            FileId = fileId,
            Token = _urlSigner.Sign(fileId, expiresAt, CurrentUser?.Id),
            ExpiresAt = expiresAt
        };
    }

    public async Task<Result<UserStorageUsage>> GetUserStorageUsageAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var stats = await _repository.AsQueryable()
            .Where(f => f.CreatorId == userId)
            .GroupBy(f => f.CreatorId)
            .Select(g => new
            {
                FileCount = g.Count(),
                TotalSize = g.Sum(f => f.Size)
            })
            .FirstOrDefaultAsync(cancellationToken);

        var usage = new UserStorageUsage
        {
            UserId = userId,
            FileCount = stats?.FileCount ?? 0,
            TotalSize = stats?.TotalSize ?? 0
        };

        return Ok(usage);
    }

    public async Task<Result<IEnumerable<UserStorageUsage>>> GetTopUsersByStorageAsync(int top = 20, CancellationToken cancellationToken = default)
    {
        if (top <= 0)
            return Fail<IEnumerable<UserStorageUsage>>("Top must be greater than 0", 400, ErrorCodes.VALIDATION_ERROR);

        var usages = await _repository.AsQueryable()
            .Where(f => f.CreatorId != null)
            .GroupBy(f => f.CreatorId)
            .Select(g => new UserStorageUsage
            {
                UserId = g.Key,
                FileCount = g.Count(),
                TotalSize = g.Sum(f => f.Size)
            })
            .OrderByDescending(u => u.TotalSize)
            .Take(top)
            .ToListAsync(cancellationToken);

        return Ok((IEnumerable<UserStorageUsage>)usages);
    }

    public async Task<Result<FileIntegrityResult>> VerifyFileIntegrityAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        var record = await _repository.GetAsync(fileId, cancellationToken);
        if (record == null)
            return Fail<FileIntegrityResult>("File record not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);

        var result = await VerifySingleFileIntegrityAsync(record, cancellationToken);
        return Ok(result);
    }

    public async Task<Result<BatchIntegrityResult>> BatchVerifyIntegrityAsync(int maxFiles = 100, CancellationToken cancellationToken = default)
    {
        var query = _repository.AsQueryable().OrderBy(f => f.CreationTime);
        var files = maxFiles > 0
            ? await query.Take(maxFiles).ToListAsync(cancellationToken)
            : await query.ToListAsync(cancellationToken);

        var batch = new BatchIntegrityResult { TotalChecked = files.Count };

        foreach (var file in files)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var result = await VerifySingleFileIntegrityAsync(file, cancellationToken);
            switch (result.Status)
            {
                case FileIntegrityStatus.Healthy:
                    batch.Healthy++;
                    break;
                case FileIntegrityStatus.Missing:
                    batch.Missing++;
                    batch.Problems.Add(result);
                    break;
                case FileIntegrityStatus.Corrupted:
                    batch.Corrupted++;
                    batch.Problems.Add(result);
                    break;
                case FileIntegrityStatus.Error:
                    batch.Errors++;
                    batch.Problems.Add(result);
                    break;
            }
        }

        LogInformation("Batch integrity check: {Total} checked, {Healthy} healthy, {Missing} missing, {Corrupted} corrupted, {Errors} errors",
            batch.TotalChecked, batch.Healthy, batch.Missing, batch.Corrupted, batch.Errors);

        return Ok(batch);
    }

    public async Task<Result<FileRecord>> SetFileTagsAsync(Guid fileId, List<string> tags, CancellationToken cancellationToken = default)
    {
        var record = await _repository.GetAsync(fileId, cancellationToken);
        var check = await EnsureWritableAsync<FileRecord>(record, cancellationToken);
        if (check != null)
            return check;

        record!.SetTagsList(tags);
        await _repository.UpdateAsync(record, cancellationToken);

        LogInformation("File tags updated: {FileId}, Tags: {Tags}", fileId, record.Tags ?? string.Empty);
        return Ok(record, "File tags updated successfully");
    }

    public async Task<Result<FileRecord>> SetMetadataAsync(Guid fileId, Dictionary<string, string> metadata, CancellationToken cancellationToken = default)
    {
        var record = await _repository.GetAsync(fileId, cancellationToken);
        var check = await EnsureWritableAsync<FileRecord>(record, cancellationToken);
        if (check != null)
            return check;

        // Validate metadata size before saving
        var serialized = JsonSerializer.Serialize(metadata);
        if (serialized.Length > 4096)
            return Fail<FileRecord>($"Metadata JSON exceeds maximum allowed size (4096 chars). Current size: {serialized.Length} chars.", 400, ErrorCodes.VALIDATION_ERROR);

        record!.SetMetadata(metadata);
        await _repository.UpdateAsync(record, cancellationToken);

        LogInformation("File metadata updated: {FileId}, Keys: {Keys}", fileId, metadata.Count > 0 ? string.Join(", ", metadata.Keys) : "(cleared)");
        return Ok(record, "File metadata updated successfully");
    }

    public async Task<Result<Dictionary<string, string>>> GetMetadataAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        var record = await _repository.GetAsync(fileId, cancellationToken);
        var check = await EnsureReadableAsync<Dictionary<string, string>>(record, cancellationToken);
        if (check != null)
            return check;

        return Ok(record!.GetMetadata());
    }

    public async Task<Result<FileRecord>> SetFileVisibilityAsync(Guid fileId, bool isPublic, CancellationToken cancellationToken = default)
    {
        var record = await _repository.GetAsync(fileId, cancellationToken);

        // 变更权限而非读取权限：把私密文件改成人人可读是一次授权决策，
        // 只有能改这个文件的人才有资格做（无权者同样以 404 掩盖存在性）。
        var check = await EnsureWritableAsync<FileRecord>(record, cancellationToken);
        if (check != null)
            return check;

        if (record!.IsPublic == isPublic)
        {
            return Ok(record, "File visibility unchanged");
        }

        record.IsPublic = isPublic;
        await _repository.UpdateAsync(record, cancellationToken);

        LogInformation("File visibility changed: {FileId}, IsPublic: {IsPublic}", fileId, isPublic);
        return Ok(record, isPublic ? "File is now publicly readable" : "File is no longer publicly readable");
    }

    public async Task<Result<int>> SyncPublicFlagsFromReferencesAsync(CancellationToken cancellationToken = default)
    {
        var publicFields = _publicFieldResolver.GetPublicFileFields();
        if (publicFields.Count == 0)
        {
            LogInformation("No [FileField(Public = true)] declarations found, nothing to backfill");
            return Ok(0);
        }

        // 分成两个平行数组交给数据库：EF 不会把自定义 record struct 的集合翻译成 SQL。
        // (EntityType, FieldName) 的笛卡尔积可能匹配到多余组合，故回读后再按真实声明精确过滤。
        var entityTypes = publicFields.Select(f => f.EntityType).Distinct().ToList();
        var fieldNames = publicFields.Select(f => f.FieldName).Distinct().ToList();

        var candidates = await _referenceRepository.AsQueryable()
            .Where(r => entityTypes.Contains(r.EntityType) && fieldNames.Contains(r.FieldName))
            .Select(r => new { r.EntityType, r.FieldName, r.FileId })
            .Distinct()
            .ToListAsync(cancellationToken);

        var declared = publicFields.ToHashSet();
        var fileIds = candidates
            .Where(c => declared.Contains(new PublicFileField(c.EntityType, c.FieldName)))
            .Select(c => c.FileId)
            .Distinct()
            .ToList();

        if (fileIds.Count == 0)
        {
            return Ok(0);
        }

        // 只升不降：仅挑出仍为私密的记录改成公开，已公开的不重复写（幂等）。
        var records = await _repository
            .ToListAsync(f => fileIds.Contains(f.Id) && !f.IsPublic, cancellationToken);

        foreach (var record in records)
        {
            record.IsPublic = true;
            await _repository.UpdateAsync(record, cancellationToken);
        }

        LogInformation("Public flag backfill: {Count} files marked public from {FieldCount} declared public fields",
            records.Count, publicFields.Count);
        return Ok(records.Count, $"{records.Count} files marked publicly readable");
    }

    public async Task<Result<ThumbnailBackfillResult>> BackfillThumbnailsAsync(
        IReadOnlyCollection<Guid>? fileIds = null, int maxFiles = 100, CancellationToken cancellationToken = default)
    {
        var result = new ThumbnailBackfillResult();

        // 候选 = 没有缩略图 + 有对象 + 扩展名是生成器**此刻**画得出的。最后一条按扩展名列表进 SQL 而不是
        // 取回全表再问 CanGenerate：没有缩略图的记录里绝大多数是 .txt / .zip / .docx，本来就画不出来。
        var supported = _thumbnails.SupportedExtensions.ToList();
        if (supported.Count == 0)
        {
            return Ok(result, "No thumbnail source is available on this host");
        }

        var query = _repository.AsQueryable()
            .Where(f => f.ThumbnailPath == null && f.Path != null && supported.Contains(f.Extension.ToLower()));

        if (fileIds is { Count: > 0 })
        {
            var ids = fileIds.ToList();
            query = query.Where(f => ids.Contains(f.Id));
        }

        var total = await query.CountAsync(cancellationToken);
        var ordered = query.OrderBy(f => f.CreationTime);
        var batch = maxFiles > 0
            ? await ordered.Take(maxFiles).ToListAsync(cancellationToken)
            : await ordered.ToListAsync(cancellationToken);

        result.Scanned = batch.Count;

        foreach (var record in batch)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var thumbnailPath = await _thumbnails.GenerateAsync(record.Path!, record.FileName, record.Extension, record.Size, cancellationToken);
            if (thumbnailPath == null)
            {
                // 画不出来的原因已经在生成器里记了日志；它仍是候选，下一次调用还会再试 ——
                // 所以调用方按 Generated 归零而不是 Remaining 归零停手。
                result.Failed++;
                result.FailedFileIds.Add(record.Id);
                continue;
            }

            record.ThumbnailPath = thumbnailPath;
            await _repository.UpdateAsync(record, cancellationToken);
            result.Generated++;
        }

        result.Remaining = total - result.Generated;

        LogInformation("Thumbnail backfill: {Scanned} scanned, {Generated} generated, {Failed} failed, {Remaining} still without a thumbnail",
            result.Scanned, result.Generated, result.Failed, result.Remaining);
        return Ok(result, $"{result.Generated} thumbnails generated");
    }

    public async Task<Result<IPagedList<FileRecord>>> GetFilesByTagAsync(string tag, int pageIndex = 1, int pageSize = 20, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return Fail<IPagedList<FileRecord>>("Tag cannot be empty", 400, ErrorCodes.VALIDATION_ERROR);

        var normalizedTag = tag.Trim().ToLower();

        // Use LIKE query for comma-separated tags column
        var query = _repository.AsQueryable()
            .Where(f => f.Tags != null && f.Tags.ToLower().Contains(normalizedTag))
            .OrderByDescending(f => f.CreationTime);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        // Post-filter: exact tag match (not substring match)
        items = items.Where(f => f.GetTagsList().Any(t => t.Equals(tag.Trim(), StringComparison.OrdinalIgnoreCase))).ToList();

        IPagedList<FileRecord> pagedList = new PagedList<FileRecord>(items, pageIndex, pageSize, total);
        return Ok(pagedList);
    }

    #region Private Methods

    /// <summary>
    /// 取流的字节长度；流不可 seek（网络流）或已被关闭时返回 null，而不是抛异常。
    /// 必须在把流交给 provider **之前**调用，见 <see cref="IFileStorage.UploadAsync"/> 的流所有权约定。
    /// </summary>
    private static long? TryGetStreamLength(Stream stream)
    {
        if (!stream.CanSeek)
            return null;

        try
        {
            return stream.Length;
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// 文件记录里的 Size：优先用上传**之前**测得的长度；测不出来（不可 seek 的流）时
    /// 回问 provider，由它按已落盘的对象报大小。两者都拿不到就记 0 并留一条 Warning：
    /// 大小是描述性字段，不该在文件已经存好之后把整次保存变成一次失败。
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

    /// <summary>
    /// 验证文件（大小和类型）
    /// </summary>
    private Result<T>? ValidateFile<T>(string fileName, Stream stream)
        // 不可 seek 的流量不出大小，此时 TryGetStreamLength 返回 null、闸门放行
        // （拦不住也不该为此抛 NotSupportedException）。这类请求的兜底在更外层：
        // StorageModule 已按 MaxFileSize 放开并限制了请求体上限。
        => _guard.Validate<T>(fileName, TryGetStreamLength(stream));

    /// <summary>
    /// 尝试通过 MD5 获取已存在的文件。
    /// </summary>
    /// <remarks>
    /// <paramref name="isTemporary"/> 是本次上传的形态，不是命中记录的形态。一条由临时上传创建的记录
    /// （<c>IsTemporary = true</c>，计数 = 正式引用行数）被一次**正式**上传复用后，就有了一个不留引用行的
    /// 隐式持有者 —— 那正是正式记录的形状，所以记录随之转正（<c>IsTemporary = false</c>）。
    /// 不转正的话，「只重算临时记录」的 <c>SyncAllReferenceCountsAsync</c> 会按引用行把它归零，
    /// 默认开启的孤儿回收随后物理删除正式上传者的文件。
    /// </remarks>
    private async Task<Result<FileRecord>?> TryGetExistingFileByMd5Async(
        string md5Hash, string originalFileName, Stream stream, bool isTemporary, bool isPublic = false, List<string>? uploadedObjects = null)
    {
        var match = await _repository.FindAsync(f => f.Md5Hash == md5Hash);
        if (match == null)
        {
            return null;
        }

        // 按 id 再取一次（GetAsync → DbSet.FindAsync 先问变更跟踪器，命中就不发查询）。
        // 上面那句按谓词查走的是 AsNoTracking，拿到的是脱离跟踪的**新实例**，
        // 它的 ReferenceCount 是数据库里的值 —— 同一个作用域内第二次命中同一行时，
        // 前一次的 ++ 可能还没落库（调用方开着事务就一定没有），在这个旧值上再 ++
        // 只会把上一次的递增覆盖掉：引用计数少算，清理任务随后删掉仍被引用的文件。
        // 与 FileShareService.LoadForUpdateAsync 同一路子。
        var existing = await _repository.GetAsync(match.Id) ?? match;

        // 内容相同但可见性诉求不同：本次上传要公开，命中的记录却是私密的。
        // 复用它等于把**别人的**私密文件一并改成人人可读 —— 上传者持有相同的字节
        // 不代表他有权把那条记录对外开放。宁可多存一份，也不做这次提权。
        if (isPublic && !existing.IsPublic)
        {
            LogInformation("Skipping MD5 reuse for a public upload: existing record {FileId} is private", existing.Id);
            return null;
        }

        if (!isTemporary && existing.IsTemporary)
        {
            LogInformation("Temporary record {FileId} reused by a permanent upload; marking it permanent", existing.Id);
            existing.IsTemporary = false;
        }

        var fileExists = await _storage.ExistsAsync(GetSafePath(existing.Path));
        if (fileExists)
        {
            existing.ReferenceCount++;
            await _repository.UpdateAsync(existing);
            LogInformation("File reused by MD5: {FileName}, OriginalName: {OriginalName}", existing.FileName, originalFileName);
            await PublishFileUploadedEventAsync(existing, isReused: true);
            return Ok(existing, "File reused by MD5");
        }
        else
        {
            LogWarning("File record exists but physical file missing, re-uploading: {FileName}", existing.FileName);

            var existingContentType = FileTypeHelper.GetContentType(existing.Extension ?? Path.GetExtension(originalFileName));
            stream.Position = 0;
            var newFilePath = await _storage.UploadAsync(existing.FileName, stream, existingContentType);
            uploadedObjects?.Add(newFilePath);

            existing.Path = newFilePath;
            existing.ReferenceCount++;

            if (ShouldAutoGenerateThumbnail(existing.Extension))
            {
                // 缩略图是从 newFilePath 回读生成的，不碰 stream。上传之后这个流可能已经
                // 被 provider 关掉，回退它的位置只会白白抛 ObjectDisposedException。
                existing.ThumbnailPath = await _thumbnails.GenerateAsync(newFilePath, existing.FileName, existing.Extension, existing.Size);
                if (existing.ThumbnailPath != null)
                    uploadedObjects?.Add(existing.ThumbnailPath);
            }

            await _repository.UpdateAsync(existing);
            LogInformation("File re-uploaded and record updated: {FileName}, OriginalName: {OriginalName}", existing.FileName, originalFileName);
            return Ok(existing, "File re-uploaded (was missing)");
        }
    }

    /// <summary>
    /// 创建文件引用（内部使用，用于 SaveWithReferenceAsync）
    /// </summary>
    private async Task CreateReferenceAsync(Guid fileId, string entityType, Guid entityId, string fieldName, bool isTemporary, CancellationToken cancellationToken = default)
    {
        var reference = new FileReference
        {
            FileId = fileId,
            EntityType = entityType,
            EntityId = entityId,
            FieldName = fieldName,
            IsTemporary = isTemporary
        };

        await _referenceRepository.InsertAsync(reference, cancellationToken);

        if (!isTemporary)
        {
            var fileRecord = await _repository.GetAsync(fileId, cancellationToken);
            if (fileRecord != null)
            {
                fileRecord.ReferenceCount++;
                await _repository.UpdateAsync(fileRecord, cancellationToken);
            }
        }
    }

    private Result<T>? EnsureFileRecordExists<T>(FileRecord? fileRecord)
    {
        if (fileRecord == null)
        {
            return Fail<T>("File record not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }
        return null;
    }

    /// <summary>
    /// 存在性 + 读权限。不可读时返回 **404 而非 403** —— 403 会告诉调用者"这个 id 上确实有东西",
    /// 顺序 GUID 下这本身就是可枚举的信息。
    /// </summary>
    private async Task<Result<T>?> EnsureReadableAsync<T>(FileRecord? fileRecord, CancellationToken cancellationToken = default)
    {
        var missing = EnsureFileRecordExists<T>(fileRecord);
        if (missing != null)
            return missing;

        if (await _accessAuthorizer.CanReadAsync(fileRecord!, cancellationToken))
            return null;

        return Fail<T>("File record not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
    }

    /// <summary>
    /// 存在性 + **签发**访问令牌的权限。与读取判据同源,但不认「签名令牌」那一条 ——
    /// 渲染凭据不该能自我续期(见 <c>IFileAccessAuthorizer.CanMintAccessTokenAsync</c>)。
    /// </summary>
    private async Task<Result<T>?> EnsureMintableAsync<T>(FileRecord? fileRecord, CancellationToken cancellationToken = default)
    {
        var missing = EnsureFileRecordExists<T>(fileRecord);
        if (missing != null)
            return missing;

        if (await _accessAuthorizer.CanMintAccessTokenAsync(fileRecord!, cancellationToken))
            return null;

        return Fail<T>("File record not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
    }

    /// <summary>
    /// 存在性 + 变更权限(删除 / 改名 / 改标签或元数据)。同样以 404 掩盖存在性。
    /// </summary>
    private async Task<Result<T>?> EnsureWritableAsync<T>(FileRecord? fileRecord, CancellationToken cancellationToken = default)
    {
        var missing = EnsureFileRecordExists<T>(fileRecord);
        if (missing != null)
            return missing;

        if (await _accessAuthorizer.CanWriteAsync(fileRecord!, cancellationToken))
            return null;

        return Fail<T>("File record not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
    }

    private const string PresignedGetVerb = "GET";
    private const string PresignedPutVerb = "PUT";

    /// <summary>
    /// 预签名 URL 只有两种动词：GET（读凭据）与 PUT（写凭据）。返回规范化的大写形态，
    /// 其它一律 <c>null</c>。provider 各自做的是不区分大小写的比较，但判据、日志与
    /// 交给 provider 的必须是同一个形态。
    /// </summary>
    private static string? NormalizePresignedVerb(string? httpMethod)
    {
        if (string.Equals(httpMethod, PresignedGetVerb, StringComparison.OrdinalIgnoreCase))
            return PresignedGetVerb;
        if (string.Equals(httpMethod, PresignedPutVerb, StringComparison.OrdinalIgnoreCase))
            return PresignedPutVerb;
        return null;
    }

    private static string GetSafePath(string? path)
    {
        return path ?? string.Empty;
    }

    /// <summary>
    /// 解压中途作废：把已经交给 provider 的对象删掉。记录此时还没落库（整批在最后一次
    /// <c>InsertManyAsync</c>），所以这里只需要收拾物理对象。删除失败只记日志：清理任务会按
    /// 「无记录的孤儿对象」再兜一次。
    /// </summary>
    private async Task DiscardExtractedAsync(List<FileRecord> extracted)
    {
        foreach (var record in extracted)
        {
            try
            {
                await _storage.DeleteAsync(GetSafePath(record.Path));
            }
            catch (Exception ex)
            {
                LogWarning("Failed to discard extracted object {Path} after aborting decompression: {Error}", record.Path, ex.Message);
            }
        }

        extracted.Clear();
    }

    /// <summary>
    /// 压缩包的展示名：调用方没给就按时间戳造一个；给了就补齐 <c>.zip</c> 后缀 ——
    /// 记录的 Extension 与 ContentType 都是 zip，展示名不该与之矛盾。它只用于下载时的文件名，
    /// <b>不参与存储键</b>。
    /// </summary>
    private static string ResolveArchiveDisplayName(string? requested)
    {
        if (string.IsNullOrWhiteSpace(requested))
            return $"archive_{DateTime.UtcNow:yyyyMMddHHmmss}.zip";

        var name = requested.Trim();
        return name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? name : $"{name}.zip";
    }

    private Result<T>? ValidateFileName<T>(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            return Fail<T>("FileName cannot be null or empty", 400, ErrorCodes.VALIDATION_ERROR);
        }
        return null;
    }

    private Result<T>? ValidateStream<T>(Stream? stream)
    {
        if (stream == null)
        {
            return Fail<T>("Stream cannot be null", 400, ErrorCodes.VALIDATION_ERROR);
        }
        return null;
    }

    private Result<T>? ValidateFileIds<T>(IEnumerable<Guid>? fileIds)
    {
        if (fileIds == null || !fileIds.Any())
        {
            return Fail<T>("At least one file ID is required", 400, ErrorCodes.VALIDATION_ERROR);
        }
        return null;
    }

    private static int DecrementReferenceCount(FileRecord fileRecord)
    {
        fileRecord.ReferenceCount = Math.Max(0, fileRecord.ReferenceCount - 1);
        return fileRecord.ReferenceCount;
    }

    private async Task PublishFileUploadedEventAsync(FileRecord record, bool isReused = false)
    {
        if (EventBus == null)
            return;

        await EventBus.PublishAsync(new FileUploadedEvent
        {
            FileId = record.Id,
            OriginalName = record.OriginalName ?? record.FileName,
            Size = record.Size,
            ContentType = record.ContentType,
            Provider = record.Provider ?? "Local",
            IsTemporary = record.IsTemporary,
            Md5Hash = record.Md5Hash,
            IsReused = isReused
        });
    }

    private async Task PublishFileAccessedEventAsync(Guid fileId, FileAccessType accessType)
    {
        if (EventBus == null)
            return;

        await EventBus.PublishAsync(new FileAccessedEvent
        {
            FileId = fileId,
            AccessType = accessType
        });
    }

    private async Task<FileIntegrityResult> VerifySingleFileIntegrityAsync(FileRecord record, CancellationToken cancellationToken)
    {
        var result = new FileIntegrityResult
        {
            FileId = record.Id,
            OriginalName = record.OriginalName ?? record.FileName,
            ExpectedMd5 = record.Md5Hash
        };

        try
        {
            if (string.IsNullOrEmpty(record.Path))
            {
                result.Status = FileIntegrityStatus.Missing;
                result.PhysicalFileExists = false;
                return result;
            }

            var exists = await _storage.ExistsAsync(GetSafePath(record.Path));
            result.PhysicalFileExists = exists;

            if (!exists)
            {
                result.Status = FileIntegrityStatus.Missing;
                return result;
            }

            // Verify MD5 if we have a stored hash
            if (!string.IsNullOrEmpty(record.Md5Hash))
            {
                using var stream = await _storage.DownloadAsync(GetSafePath(record.Path));
                var actualMd5 = await HashHelper.GetMd5Async(stream);
                result.ActualMd5 = actualMd5;
                result.Md5Matches = string.Equals(record.Md5Hash, actualMd5, StringComparison.OrdinalIgnoreCase);

                result.Status = result.Md5Matches == true
                    ? FileIntegrityStatus.Healthy
                    : FileIntegrityStatus.Corrupted;
            }
            else
            {
                // No MD5 stored, file exists - consider healthy
                result.Md5Matches = null;
                result.Status = FileIntegrityStatus.Healthy;
            }
        }
        catch (Exception ex)
        {
            result.Status = FileIntegrityStatus.Error;
            result.Error = ex.Message;
        }

        return result;
    }

    /// <summary>
    /// Physically delete a file (and its thumbnail), then delete the DB record only when the
    /// primary physical file is confirmed gone. If the physical delete fails while the file still
    /// exists on storage, the DB record is intentionally KEPT (ReferenceCount is already 0) so the
    /// background cleanup task (CleanupOrphanFilesAsync) can retry - this avoids leaving an
    /// orphaned physical file with no DB record. Returns true if the DB record was deleted.
    /// </summary>
    private async Task<bool> DeleteFileAsync(FileRecord record)
    {
        // Thumbnail is best-effort: failing to delete it must not block the main record cleanup.
        if (!string.IsNullOrEmpty(record.ThumbnailPath))
        {
            await _storage.DeleteAsync(record.ThumbnailPath);
        }

        if (!string.IsNullOrEmpty(record.Path))
        {
            var deleted = await _storage.DeleteAsync(record.Path);
            if (!deleted)
            {
                // DeleteAsync returns false both on real failure and when the file is already gone.
                // Only keep the DB record when the physical file genuinely still exists.
                var stillExists = await _storage.ExistsAsync(record.Path);
                if (stillExists)
                {
                    LogWarning(
                        "Physical file deletion failed, keeping DB record for background cleanup retry: {FileName}, Path: {Path}",
                        record.FileName, record.Path);
                    return false;
                }
            }
        }

        await _repository.DeleteAsync(record);
        return true;
    }

    #endregion
}
