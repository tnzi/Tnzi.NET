namespace Tnzi.Storage.Workspace.Services;

/// <summary>
/// 文件分块上传服务实现
/// </summary>
public class FileChunkUploadService : ApplicationService, IFileChunkUploadService
{
    private readonly IRepository<FileUploadSession, Guid> _uploadSessionRepository;
    private readonly IRepository<FileChunk, Guid> _chunkRepository;
    private readonly IRepository<FileRecord, Guid> _fileRepository;
    private readonly IFileStorage _storage;
    private readonly IOptionsMonitor<StorageOptions> _options;
    private readonly UploadGuard _guard;

    public FileChunkUploadService(
        IRepository<FileUploadSession, Guid> uploadSessionRepository,
        IRepository<FileChunk, Guid> chunkRepository,
        IRepository<FileRecord, Guid> fileRepository,
        IFileStorage storage,
        IOptionsMonitor<StorageOptions> options,
        IServiceProvider serviceProvider,
        UploadGuard guard)
        : base(serviceProvider)
    {
        _uploadSessionRepository = Check.NotNull(uploadSessionRepository);
        _chunkRepository = Check.NotNull(chunkRepository);
        _fileRepository = Check.NotNull(fileRepository);
        _storage = Check.NotNull(storage);
        _options = Check.NotNull(options);
        _guard = Check.NotNull(guard);
    }

    public async Task<Result<FileUploadSessionDto>> InitiateChunkedUploadAsync(
        string fileName,
        long totalSize,
        int chunkSize = 5 * 1024 * 1024,
        string? md5Hash = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(fileName))
            return Fail<FileUploadSessionDto>("FileName cannot be null or empty", 400, ErrorCodes.VALIDATION_ERROR);

        if (totalSize <= 0)
            return Fail<FileUploadSessionDto>("TotalSize must be greater than 0", 400, ErrorCodes.VALIDATION_ERROR);

        if (chunkSize <= 0)
            return Fail<FileUploadSessionDto>("ChunkSize must be greater than 0", 400, ErrorCodes.VALIDATION_ERROR);

        // 扩展名白名单在这里拦，是为了在客户端开始推第一个分块**之前**就拒绝。
        // ★ 刻意不套 MaxFileSize：文档明确把分片上传推荐给超大文件（见 docs/modules/storage.md），
        //   在这条通道上套用单文件上限会打断按文档办事的部署。
        var extensionCheck = _guard.ValidateExtension<FileUploadSessionDto>(fileName);
        if (extensionCheck != null)
            return extensionCheck;

        // 计算总分块数
        var totalChunks = (int)Math.Ceiling((double)totalSize / chunkSize);

        // 创建上传会话（默认 24 小时过期）
        var session = new FileUploadSession
        {
            FileName = fileName,
            TotalSize = totalSize,
            ChunkSize = chunkSize,
            TotalChunks = totalChunks,
            UploadedChunks = 0,
            UploadedSize = 0,
            Md5Hash = md5Hash,
            IsCompleted = false,
            IsCancelled = false,
            ExpiresAt = DateTime.UtcNow.AddHours(24)
        };

        await _uploadSessionRepository.InsertAsync(session, cancellationToken);
        LogInformation("Chunked upload session initiated: {SessionId}, FileName: {FileName}, TotalSize: {TotalSize}", session.Id, fileName, totalSize);
        return Ok(MapToDto(session), "Chunked upload session initiated");
    }

    public async Task<Result<FileChunkDto>> UploadChunkAsync(
        Guid uploadSessionId,
        int chunkIndex,
        Stream chunkStream,
        CancellationToken cancellationToken = default)
    {
        if (chunkStream == null)
            return Fail<FileChunkDto>("Stream cannot be null", 400, ErrorCodes.VALIDATION_ERROR);

        // 验证会话是否存在且未完成
        var session = await _uploadSessionRepository.GetAsync(uploadSessionId, cancellationToken);
        if (session == null || session.IsCompleted || session.IsCancelled || !IsSessionOwner(session))
            return Fail<FileChunkDto>("Upload session is invalid or completed", 400, ErrorCodes.FILE_OPERATION_ERROR);

        // 过期判定排在归属判定**之后**：对外人说"过期了"就等于承认这个 id 上有过一个会话。
        if (IsExpired(session))
            return Fail<FileChunkDto>(SessionExpired, 400, ErrorCodes.FILE_OPERATION_ERROR);

        // 验证分块索引
        if (chunkIndex < 0 || chunkIndex >= session.TotalChunks)
            return Fail<FileChunkDto>($"ChunkIndex {chunkIndex} is out of range [0, {session.TotalChunks})", 400, ErrorCodes.VALIDATION_ERROR);

        // 检查分块是否已存在
        var existingChunk = await _chunkRepository.FindAsync(
            c => c.UploadSessionId == uploadSessionId && c.ChunkIndex == chunkIndex,
            cancellationToken);
        if (existingChunk != null)
        {
            // 如果分块已存在，删除旧的分块
            if (!string.IsNullOrEmpty(existingChunk.ChunkPath))
            {
                await _storage.DeleteAsync(existingChunk.ChunkPath);
            }
            await _chunkRepository.DeleteAsync(existingChunk, cancellationToken);
        }

        // 读取分块数据
        using var memoryStream = new MemoryStream();
        await chunkStream.CopyToAsync(memoryStream, cancellationToken);
        memoryStream.Position = 0;

        // 计算分块MD5
        var chunkMd5 = await HashHelper.GetMd5Async(memoryStream);
        memoryStream.Position = 0;

        // 保存分块到临时存储。长度在交给 provider **之前**取：上传之后这个流是否还可读
        // 由 provider 决定（见 IFileStorage.UploadAsync 的流所有权约定）。
        var chunkByteSize = memoryStream.Length;
        var chunkFileName = StorageKeyHelper.ChunkKey(uploadSessionId, chunkIndex);
        var chunkPath = await _storage.UploadAsync(chunkFileName, memoryStream, "application/octet-stream");

        // 创建分块记录
        var chunk = new FileChunk
        {
            UploadSessionId = uploadSessionId,
            ChunkIndex = chunkIndex,
            ChunkSize = chunkByteSize,
            ChunkPath = chunkPath,
            Md5Hash = chunkMd5
        };

        await _chunkRepository.InsertAsync(chunk, cancellationToken);

        // 更新会话进度
        var uploadedChunks = await _chunkRepository
            .Where(c => c.UploadSessionId == uploadSessionId)
            .CountAsync(cancellationToken);
        var uploadedSize = await _chunkRepository
            .Where(c => c.UploadSessionId == uploadSessionId)
            .SumAsync(c => c.ChunkSize, cancellationToken);

        session.UploadedChunks = uploadedChunks;
        session.UploadedSize = uploadedSize;
        await _uploadSessionRepository.UpdateAsync(session, cancellationToken);

        LogInformation("Chunk uploaded: SessionId: {SessionId}, ChunkIndex: {ChunkIndex}, Size: {Size}", uploadSessionId, chunkIndex, chunkByteSize);
        return Ok(MapToDto(chunk), "Chunk uploaded successfully");
    }

    public async Task<Result<FileRecord>> CompleteChunkedUploadAsync(
        Guid uploadSessionId,
        bool isTemporary = false,
        CancellationToken cancellationToken = default)
    {
        // 获取会话和所有分块
        var session = await _uploadSessionRepository.GetAsync(uploadSessionId, cancellationToken);
        if (session == null || session.IsCompleted || session.IsCancelled || !IsSessionOwner(session))
            return Fail<FileRecord>("Upload session is invalid or completed", 400, ErrorCodes.FILE_OPERATION_ERROR);

        if (IsExpired(session))
            return Fail<FileRecord>(SessionExpired, 400, ErrorCodes.FILE_OPERATION_ERROR);

        // 获取所有分块（按索引排序）
        var chunks = await _chunkRepository.AsQueryable()
            .Where(c => c.UploadSessionId == uploadSessionId)
            .OrderBy(c => c.ChunkIndex)
            .ToListAsync(cancellationToken);

        if (chunks.Count != session.TotalChunks)
            return Fail<FileRecord>($"Not all chunks have been uploaded. Expected {session.TotalChunks}, got {chunks.Count}", 400, ErrorCodes.FILE_OPERATION_ERROR);

        // 合并之前先确认每一片的对象还在。清理任务先删物理分片再删会话行，中间失败会留下
        // 「行在、分片不在」的会话；对象存储也可能自己弄丢对象。不先问一遍，下面的 DownloadAsync
        // 会以 provider 的异常（500）收场，而客户端需要的是一个能据以重传那一片的 400。
        foreach (var chunk in chunks)
        {
            if (string.IsNullOrEmpty(chunk.ChunkPath) || !await _storage.ExistsAsync(chunk.ChunkPath))
                return Fail<FileRecord>($"Chunk {chunk.ChunkIndex} is no longer available. Upload it again before completing.", 400, ErrorCodes.FILE_OPERATION_ERROR);
        }

        // 使用临时文件合并分块，避免大文件占用大量内存
        var tempFilePath = Path.GetTempFileName();
        try
        {
            long mergedSize;
            using (var mergedStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.ReadWrite, System.IO.FileShare.None))
            {
                foreach (var chunk in chunks)
                {
                    // 回读分片并校验其完整性（受 EnableMd5Validation 控制）。
                    // 损坏的分片在合并时会被检测出来，避免静默合出坏文件。
                    if (_options.CurrentValue.EnableMd5Validation && !string.IsNullOrEmpty(chunk.Md5Hash))
                    {
                        using var verifyStream = await _storage.DownloadAsync(chunk.ChunkPath!);
                        var actualChunkMd5 = await HashHelper.GetMd5Async(verifyStream);
                        if (!string.Equals(actualChunkMd5, chunk.Md5Hash, StringComparison.OrdinalIgnoreCase))
                        {
                            LogWarning("Chunk MD5 mismatch during merge: SessionId={SessionId}, ChunkIndex={ChunkIndex}, Expected={Expected}, Actual={Actual}",
                                uploadSessionId, chunk.ChunkIndex, chunk.Md5Hash, actualChunkMd5);
                            return Fail<FileRecord>($"Chunk {chunk.ChunkIndex} integrity check failed (MD5 mismatch)", 400, ErrorCodes.VALIDATION_ERROR);
                        }
                    }

                    using var chunkStream = await _storage.DownloadAsync(chunk.ChunkPath!);
                    await chunkStream.CopyToAsync(mergedStream, cancellationToken);
                }

                mergedSize = mergedStream.Length;

                // 验证文件大小
                if (mergedSize != session.TotalSize)
                    return Fail<FileRecord>($"File size mismatch. Expected {session.TotalSize}, got {mergedSize}", 400, ErrorCodes.FILE_OPERATION_ERROR);

                // 会话建立时已经拦过一次；这里再拦一次是因为白名单可以在上传途中被改，
                // 而落库的判据应当是**写下去那一刻**的策略。
                var extensionCheck = _guard.ValidateExtension<FileRecord>(session.FileName);
                if (extensionCheck != null)
                    return extensionCheck;

                var extension = Path.GetExtension(session.FileName);
                var contentType = FileTypeHelper.GetContentType(extension);

                // ★ 净化管线必须跑在算 MD5 与交给 provider **之前**：净化器可以改写内容，
                //   基于原始字节算出的哈希与实际落库的字节对不上 ⇒ 去重命中错误记录、
                //   完整性校验永远失败（与 SaveAsync 同一条理由）。
                mergedStream.Position = 0;
                await using var sanitized = await _guard.RunAsync(
                    session.FileName, extension, contentType, mergedStream);

                if (sanitized.IsRejected)
                    return Fail<FileRecord>(sanitized.Reason!, 400, ErrorCodes.VALIDATION_ERROR);

                var content = sanitized.Content;

                // 计算合并后的 MD5（受 EnableMd5Validation 控制；关闭时跳过整文件校验）
                string? md5Hash = null;
                if (_options.CurrentValue.EnableMd5Validation)
                {
                    if (content.CanSeek) content.Position = 0;
                    md5Hash = await HashHelper.GetMd5Async(content);

                    // 净化器改写过内容时，客户端声明的哈希本就对不上，比对没有意义。
                    if (ReferenceEquals(content, mergedStream)
                        && !string.IsNullOrEmpty(session.Md5Hash)
                        && md5Hash != session.Md5Hash)
                        return Fail<FileRecord>("File MD5 hash mismatch", 400, ErrorCodes.FILE_OPERATION_ERROR);
                }

                // ★ 落库的大小必须是**净化之后**那份字节的大小：净化器可以重编码或剥元数据，
                //   记下合并时的长度就等于「存的是新字节、写的是旧尺寸」（SaveAsync 同口径）。
                //   不可 seek 时退回合并长度 —— 那种流量不出来，而合并长度至少是有出处的数字。
                //   长度要在交给 provider **之前**取：上传之后流还能不能读由 provider 决定
                //   （见 IFileStorage.UploadAsync 的流所有权约定）。
                if (content.CanSeek) content.Position = 0;
                var storedSize = content.CanSeek ? content.Length : mergedSize;

                // ★ 存储键由服务端生成；session.FileName 是客户端建会话时给的名字，只做展示名。
                //   这条路径此前直接拿它当键：文档把分片上传推荐给大文件，于是两个用户同一天
                //   上传同名的 report.pdf，第二份在本地 provider 上把第一份截断覆盖、第一条记录的
                //   MD5 与大小仍是旧值；在对象存储上（键即对象名、PutObject 默认覆盖）则是任何
                //   已登录用户都能替换掉别人文件的字节（见 StorageKeyHelper）。
                var storageKey = StorageKeyHelper.NewKey(extension);
                var filePath = await _storage.UploadAsync(storageKey, content, contentType);

                // 创建文件记录。生命周期标记与直传 SaveAsync 同一口径：
                // 临时文件 IsTemporary = true 且引用计数从 0 起，正式文件从 1 起。
                // ★ 此前 isTemporary 形参从未被读取（整条纵切都在传它），记录一律落成正式文件、
                //   请求「临时」的也永远不会被临时清理回收。
                var fileRecord = new FileRecord
                {
                    FileName = storageKey,
                    OriginalName = session.FileName,
                    Extension = extension,
                    Size = storedSize,
                    Path = filePath,
                    Md5Hash = md5Hash,
                    Provider = _storage.ProviderName,
                    ContentType = contentType,
                    IsTemporary = isTemporary,
                    ReferenceCount = isTemporary ? 0 : 1
                };

                try
                {
                    await _fileRepository.InsertAsync(fileRecord, cancellationToken);
                }
                catch
                {
                    // 合并后的对象已交给 provider、记录没落成：删掉它（孤儿回收按 FileRecord 枚举，
                    // 看不见它）。分片与会话原样保留，客户端可以再 complete 一次。
                    await DiscardMergedObjectAsync(filePath);
                    throw;
                }

                // 标记会话为已完成
                session.IsCompleted = true;
                session.CompletedTime = DateTime.UtcNow;
                await _uploadSessionRepository.UpdateAsync(session, cancellationToken);

                // 清理分块文件
                foreach (var chunk in chunks)
                {
                    if (!string.IsNullOrEmpty(chunk.ChunkPath))
                    {
                        await _storage.DeleteAsync(chunk.ChunkPath);
                    }
                    await _chunkRepository.DeleteAsync(chunk.Id, cancellationToken);
                }

                LogInformation("Chunked upload completed: SessionId: {SessionId}, FileName: {FileName}, Size: {Size}", uploadSessionId, session.FileName, mergedSize);
                return Ok(fileRecord, "Chunked upload completed successfully");
            }
        }
        finally
        {
            if (File.Exists(tempFilePath))
                File.Delete(tempFilePath);
        }
    }

    public async Task<Result> CancelChunkedUploadAsync(Guid uploadSessionId, CancellationToken cancellationToken = default)
    {
        // 获取会话和所有分块
        var session = await _uploadSessionRepository.GetAsync(uploadSessionId, cancellationToken);
        if (session == null || session.IsCompleted || !IsSessionOwner(session))
            return Ok("Upload session is already completed or not found");

        var chunks = await _chunkRepository
            .ToListAsync(c => c.UploadSessionId == uploadSessionId, cancellationToken);

        // 删除所有分块文件
        foreach (var chunk in chunks)
        {
            if (!string.IsNullOrEmpty(chunk.ChunkPath))
            {
                await _storage.DeleteAsync(chunk.ChunkPath);
            }
            await _chunkRepository.DeleteAsync(chunk.Id, cancellationToken);
        }

        // 标记会话为已取消
        session.IsCancelled = true;
        await _uploadSessionRepository.UpdateAsync(session, cancellationToken);
        LogInformation("Chunked upload cancelled: SessionId: {SessionId}", uploadSessionId);
        return Ok("Chunked upload cancelled successfully");
    }

    public async Task<Result<FileUploadProgress>> GetUploadProgressAsync(Guid uploadSessionId, CancellationToken cancellationToken = default)
    {
        var session = await _uploadSessionRepository.GetAsync(uploadSessionId, cancellationToken);
        if (session == null || !IsSessionOwner(session))
            return Fail<FileUploadProgress>("Upload session not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);

        var progress = new FileUploadProgress
        {
            UploadSessionId = session.Id,
            FileName = session.FileName,
            TotalSize = session.TotalSize,
            UploadedSize = session.UploadedSize,
            TotalChunks = session.TotalChunks,
            UploadedChunks = session.UploadedChunks,
            IsCompleted = session.IsCompleted,
            IsCancelled = session.IsCancelled
        };
        return Ok(progress);
    }

    /// <summary>
    /// 这个上传会话是不是当前用户开的。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 会话 id 一直是这四个端点的<b>唯一</b>判据，而 <c>IFileAccessAuthorizer</c> 的注释早就写明
    /// 「仅凭知道 id 不足以构成授权」。id 本身不易枚举（顺序 GUID 仍留 62-74 位 CSPRNG 熵），
    /// 但它会经反向代理日志、共享的 HAR、以及对象存储里 <c>chunk_{sessionId}_{index}</c> 这样的
    /// 键名漏出去 —— 拿到一个就能在 24 小时有效期内<b>看进度、替换分块、抢先完成、或直接取消</b>
    /// 别人的上传。抢先完成尤其糟：合并出的 <c>FileRecord</c> 由审计钩子盖上<b>抢的人</b>的
    /// <c>CreatorId</c>，于是他成了这份内容的所有者，而受害者的完成请求撞上「已完成」直接失败。
    /// </para>
    /// <para>
    /// ★ <b>刻意不复用 <see cref="IFileAccessAuthorizer"/></b>：它的三个成员都吃
    /// <c>FileRecord</c>，而上传完成之前根本没有 <c>FileRecord</c>；何况它是 <c>TryAddScoped</c>
    /// 注册的、明摆着让消费方替换的扩展点，加一个成员会让每一份自定义实现编译不过。
    /// 这里复用的是它的<b>规则</b>而不是它的接口，与 <c>FileAccessAuthorizer.IsOwner</c> 逐字同构：
    /// <c>CreatorId</c> 为 null 的会话（后台任务产生）不属于任何人。
    /// </para>
    /// <para>
    /// ★ 拒绝时<b>沿用各自原有的那条回答</b>（无效会话 / 未找到 / 已完成），不新增 403：
    /// 区分开就等于告诉试探者这个 id 上确实有一个活着的会话。
    /// </para>
    /// </remarks>
    private bool IsSessionOwner(FileUploadSession session)
        => session.CreatorId.HasValue
           && CurrentUser?.Id is { } me
           && session.CreatorId.Value == me;

    private const string SessionExpired = "Upload session has expired. Start a new upload.";

    /// <summary>
    /// 会话的有效期到了没。
    /// </summary>
    /// <remarks>
    /// <c>InitiateChunkedUploadAsync</c> 给每个会话写 24 小时的 <c>ExpiresAt</c>，此前只有后台清理读它：
    /// 收分块与合并落库两条写路径都不看，于是一个 30 天前开的会话照样能收分块、能合并出一条文件记录，
    /// 直到受 <c>Storage:Cleanup:MaxFilesPerRun</c> 限速的清理任务轮到它 —— 有效期只是一个展示字段。
    /// 现在两条写路径都拒绝过期会话；<b>取消刻意放行</b>（那是收尾动作，让客户端自己清残留分片，不必等后台任务），
    /// 查进度也放行（只读，DTO 上本就带着 <c>ExpiresAt</c>）。
    /// </remarks>
    private static bool IsExpired(FileUploadSession session) => session.ExpiresAt <= DateTime.UtcNow;

    /// <summary>
    /// 删掉一个交给了 provider 却没有任何记录指向的合并对象。删除失败只记日志：调用方正在把原异常抛出去，
    /// 不能让收拾现场的异常把它盖掉。
    /// </summary>
    private async Task DiscardMergedObjectAsync(string path)
    {
        try
        {
            await _storage.DeleteAsync(path);
        }
        catch (Exception ex)
        {
            LogWarning("Failed to discard merged object {Path} after an aborted chunked upload completion: {Error}", path, ex.Message);
        }
    }

    /// <summary>
    /// 投影为对外 DTO。除 <c>TenantId</c> 外与实体逐字段一致 —— 这两个端点此前直接把实体
    /// 序列化出去，把租户标识一并发给了浏览器。
    /// </summary>
    private static FileUploadSessionDto MapToDto(FileUploadSession session) => new()
    {
        Id = session.Id,
        FileName = session.FileName,
        TotalSize = session.TotalSize,
        ChunkSize = session.ChunkSize,
        TotalChunks = session.TotalChunks,
        UploadedChunks = session.UploadedChunks,
        UploadedSize = session.UploadedSize,
        Md5Hash = session.Md5Hash,
        IsCompleted = session.IsCompleted,
        IsCancelled = session.IsCancelled,
        CompletedTime = session.CompletedTime,
        CreationTime = session.CreationTime,
        CreatorId = session.CreatorId,
        ExpiresAt = session.ExpiresAt
    };

    /// <summary>投影为对外 DTO，同 <see cref="MapToDto(FileUploadSession)"/>。</summary>
    private static FileChunkDto MapToDto(FileChunk chunk) => new()
    {
        Id = chunk.Id,
        UploadSessionId = chunk.UploadSessionId,
        ChunkIndex = chunk.ChunkIndex,
        ChunkSize = chunk.ChunkSize,
        ChunkPath = chunk.ChunkPath,
        Md5Hash = chunk.Md5Hash,
        CreationTime = chunk.CreationTime
    };
}
