namespace Tnzi.Storage.Services;

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

    public FileChunkUploadService(
        IRepository<FileUploadSession, Guid> uploadSessionRepository,
        IRepository<FileChunk, Guid> chunkRepository,
        IRepository<FileRecord, Guid> fileRepository,
        IFileStorage storage,
        IOptionsMonitor<StorageOptions> options,
        IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        _uploadSessionRepository = Check.NotNull(uploadSessionRepository);
        _chunkRepository = Check.NotNull(chunkRepository);
        _fileRepository = Check.NotNull(fileRepository);
        _storage = Check.NotNull(storage);
        _options = Check.NotNull(options);
    }

    public async Task<Result<FileUploadSession>> InitiateChunkedUploadAsync(
        string fileName,
        long totalSize,
        int chunkSize = 5 * 1024 * 1024,
        string? md5Hash = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(fileName))
            return Fail<FileUploadSession>("FileName cannot be null or empty", 400, ErrorCodes.VALIDATION_ERROR);

        if (totalSize <= 0)
            return Fail<FileUploadSession>("TotalSize must be greater than 0", 400, ErrorCodes.VALIDATION_ERROR);

        if (chunkSize <= 0)
            return Fail<FileUploadSession>("ChunkSize must be greater than 0", 400, ErrorCodes.VALIDATION_ERROR);

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
        return Ok(session, "Chunked upload session initiated");
    }

    public async Task<Result<FileChunk>> UploadChunkAsync(
        Guid uploadSessionId,
        int chunkIndex,
        Stream chunkStream,
        CancellationToken cancellationToken = default)
    {
        if (chunkStream == null)
            return Fail<FileChunk>("Stream cannot be null", 400, ErrorCodes.VALIDATION_ERROR);

        // 验证会话是否存在且未完成
        var session = await _uploadSessionRepository.GetAsync(uploadSessionId, cancellationToken);
        if (session == null || session.IsCompleted || session.IsCancelled)
            return Fail<FileChunk>("Upload session is invalid or completed", 400, ErrorCodes.FILE_OPERATION_ERROR);

        // 验证分块索引
        if (chunkIndex < 0 || chunkIndex >= session.TotalChunks)
            return Fail<FileChunk>($"ChunkIndex {chunkIndex} is out of range [0, {session.TotalChunks})", 400, ErrorCodes.VALIDATION_ERROR);

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
        var chunkFileName = $"chunk_{uploadSessionId}_{chunkIndex}";
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
        return Ok(chunk, "Chunk uploaded successfully");
    }

    public async Task<Result<FileRecord>> CompleteChunkedUploadAsync(
        Guid uploadSessionId,
        bool isTemporary = false,
        CancellationToken cancellationToken = default)
    {
        // 获取会话和所有分块
        var session = await _uploadSessionRepository.GetAsync(uploadSessionId, cancellationToken);
        if (session == null || session.IsCompleted || session.IsCancelled)
            return Fail<FileRecord>("Upload session is invalid or completed", 400, ErrorCodes.FILE_OPERATION_ERROR);

        // 获取所有分块（按索引排序）
        var chunks = await _chunkRepository.AsQueryable()
            .Where(c => c.UploadSessionId == uploadSessionId)
            .OrderBy(c => c.ChunkIndex)
            .ToListAsync(cancellationToken);

        if (chunks.Count != session.TotalChunks)
            return Fail<FileRecord>($"Not all chunks have been uploaded. Expected {session.TotalChunks}, got {chunks.Count}", 400, ErrorCodes.FILE_OPERATION_ERROR);

        // 使用临时文件合并分块，避免大文件占用大量内存
        var tempFilePath = Path.GetTempFileName();
        try
        {
            long mergedSize;
            using (var mergedStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.ReadWrite, System.IO.FileShare.None))
            {
                foreach (var chunk in chunks)
                {
                    if (string.IsNullOrEmpty(chunk.ChunkPath))
                        throw new StorageException($"Chunk {chunk.ChunkIndex} path is empty.", null, ErrorCodes.FILE_OPERATION_ERROR);

                    // 回读分片并校验其完整性（受 EnableMd5Validation 控制）。
                    // 损坏的分片在合并时会被检测出来，避免静默合出坏文件。
                    if (_options.CurrentValue.EnableMd5Validation && !string.IsNullOrEmpty(chunk.Md5Hash))
                    {
                        using var verifyStream = await _storage.DownloadAsync(chunk.ChunkPath);
                        var actualChunkMd5 = await HashHelper.GetMd5Async(verifyStream);
                        if (!string.Equals(actualChunkMd5, chunk.Md5Hash, StringComparison.OrdinalIgnoreCase))
                        {
                            LogWarning("Chunk MD5 mismatch during merge: SessionId={SessionId}, ChunkIndex={ChunkIndex}, Expected={Expected}, Actual={Actual}",
                                uploadSessionId, chunk.ChunkIndex, chunk.Md5Hash, actualChunkMd5);
                            return Fail<FileRecord>($"Chunk {chunk.ChunkIndex} integrity check failed (MD5 mismatch)", 400, ErrorCodes.VALIDATION_ERROR);
                        }
                    }

                    using var chunkStream = await _storage.DownloadAsync(chunk.ChunkPath);
                    await chunkStream.CopyToAsync(mergedStream, cancellationToken);
                }

                mergedSize = mergedStream.Length;

                // 验证文件大小
                if (mergedSize != session.TotalSize)
                    return Fail<FileRecord>($"File size mismatch. Expected {session.TotalSize}, got {mergedSize}", 400, ErrorCodes.FILE_OPERATION_ERROR);

                // 计算合并后的 MD5（受 EnableMd5Validation 控制；关闭时跳过整文件校验）
                string? md5Hash = null;
                if (_options.CurrentValue.EnableMd5Validation)
                {
                    mergedStream.Position = 0;
                    md5Hash = await HashHelper.GetMd5Async(mergedStream);

                    if (!string.IsNullOrEmpty(session.Md5Hash) && md5Hash != session.Md5Hash)
                        return Fail<FileRecord>("File MD5 hash mismatch", 400, ErrorCodes.FILE_OPERATION_ERROR);
                }

                // 保存合并后的文件
                mergedStream.Position = 0;
                var contentType = FileTypeHelper.GetContentType(Path.GetExtension(session.FileName));
                var filePath = await _storage.UploadAsync(session.FileName, mergedStream, contentType);

                // 创建文件记录
                var fileRecord = new FileRecord
                {
                    FileName = session.FileName,
                    OriginalName = session.FileName,
                    Extension = Path.GetExtension(session.FileName),
                    Size = mergedSize,
                    Path = filePath,
                    Md5Hash = md5Hash,
                    Provider = _storage.ProviderName,
                    ContentType = contentType,
                    ReferenceCount = 0
                };

                await _fileRepository.InsertAsync(fileRecord, cancellationToken);

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
        if (session == null || session.IsCompleted)
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
        if (session == null)
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
}
