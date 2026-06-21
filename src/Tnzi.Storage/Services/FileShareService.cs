namespace Tnzi.Storage.Services;

/// <summary>
/// 文件分享服务实现
/// </summary>
public class FileShareService : ApplicationService, IFileShareService
{
    private readonly IRepository<FileShare, Guid> _shareRepository;
    private readonly IRepository<FileRecord, Guid> _fileRepository;

    public FileShareService(
        IRepository<FileShare, Guid> shareRepository,
        IRepository<FileRecord, Guid> fileRepository,
        IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        _shareRepository = Check.NotNull(shareRepository);
        _fileRepository = Check.NotNull(fileRepository);
    }

    public async Task<Result<FileShare>> CreateShareAsync(Guid fileId, DateTime? expiresAt = null, int? maxAccessCount = null, string? password = null, CancellationToken cancellationToken = default)
    {
        var fileRecord = await _fileRepository.GetAsync(fileId, cancellationToken);
        if (fileRecord == null)
            return Fail<FileShare>("File not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);

        // 生成唯一的分享令牌
        var shareToken = GenerateShareToken();

        // 计算密码哈希（如果需要）
        string? passwordHash = null;
        if (!string.IsNullOrEmpty(password))
        {
            passwordHash = ComputePasswordHash(password);
        }

        var share = new FileShare
        {
            FileId = fileId,
            ShareToken = shareToken,
            ExpiresAt = expiresAt,
            MaxAccessCount = maxAccessCount,
            RequirePassword = !string.IsNullOrEmpty(password),
            PasswordHash = passwordHash,
            IsEnabled = true,
            AccessCount = 0
        };

        await _shareRepository.InsertAsync(share, cancellationToken);
        LogInformation("File share created: FileId: {FileId}, ShareToken: {ShareToken}", fileId, shareToken);
        return Ok(share, "File share created successfully");
    }

    public async Task<Result<FileShare>> GetShareAsync(string shareToken, CancellationToken cancellationToken = default)
    {
        var share = await _shareRepository.FindAsync((FileShare s) => s.ShareToken == shareToken, cancellationToken);
        if (share == null)
            return Fail<FileShare>("Share not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        return Ok(share);
    }

    public async Task<Result> RevokeShareAsync(string shareToken, CancellationToken cancellationToken = default)
    {
        var share = await _shareRepository.FindAsync((FileShare s) => s.ShareToken == shareToken, cancellationToken);
        if (share == null)
            return Fail("Share not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);

        share.IsEnabled = false;
        await _shareRepository.UpdateAsync(share, cancellationToken);
        LogInformation("File share revoked: ShareToken: {ShareToken}", shareToken);
        return Ok("File share revoked successfully");
    }

    public async Task<Result<bool>> ValidateShareAccessAsync(string shareToken, string? password = null, CancellationToken cancellationToken = default)
    {
        var share = await _shareRepository.FindAsync((FileShare s) => s.ShareToken == shareToken, cancellationToken);
        if (share == null || !share.IsEnabled)
            return Ok(false);

        // 检查是否过期
        if (share.ExpiresAt.HasValue && share.ExpiresAt.Value < DateTime.UtcNow)
            return Ok(false);

        // 检查是否超过最大访问次数
        if (share.MaxAccessCount.HasValue && share.AccessCount >= share.MaxAccessCount.Value)
            return Ok(false);

        // 检查密码
        if (share.RequirePassword)
        {
            if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(share.PasswordHash))
                return Ok(false);

            if (!VerifyPasswordHash(password, share.PasswordHash))
                return Ok(false);
        }

        return Ok(true);
    }

    public async Task<Result<bool>> IncrementShareAccessCountAsync(string shareToken, CancellationToken cancellationToken = default)
    {
        // 原子 check-and-increment：单条 SQL 同时校验"启用 + 未超过 MaxAccessCount"并自增，
        // WHERE 条件确保仅在仍有配额时才更新；受影响行数 > 0 表示成功占用一次配额。
        var affectedRows = await _shareRepository.AsQueryable(withTracking: false)
            .Where(s => s.ShareToken == shareToken
                && s.IsEnabled
                && (s.MaxAccessCount == null || s.AccessCount < s.MaxAccessCount))
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.AccessCount, x => x.AccessCount + 1), cancellationToken);

        return Ok(affectedRows > 0);
    }

    public async Task<Result<IEnumerable<FileShareSummaryDto>>> GetSharesByFileAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        var shares = await _shareRepository.AsQueryable()
            .Where(s => s.FileId == fileId)
            .OrderByDescending(s => s.CreationTime)
            .ToListAsync(cancellationToken);

        // Get file original name for enrichment
        var file = await _fileRepository.GetAsync(fileId, cancellationToken);
        var originalName = file?.OriginalName ?? file?.FileName ?? string.Empty;

        var dtos = shares.Select(s => MapToShareSummary(s, originalName)).ToList();
        return Ok((IEnumerable<FileShareSummaryDto>)dtos);
    }

    public async Task<Result<IPagedList<FileShareSummaryDto>>> GetActiveSharesAsync(ActiveSharesQueryRequest request, CancellationToken cancellationToken = default)
    {
        var query = _shareRepository.AsQueryable();

        if (request.FileId.HasValue)
            query = query.Where(s => s.FileId == request.FileId.Value);

        if (request.CreatorId.HasValue)
            query = query.Where(s => s.CreatorId == request.CreatorId.Value);

        if (!request.IncludeDisabled)
            query = query.Where(s => s.IsEnabled);

        if (!request.IncludeExpired)
            query = query.Where(s => s.ExpiresAt == null || s.ExpiresAt > DateTime.UtcNow);

        query = query.OrderByDescending(s => s.CreationTime);

        var total = await query.CountAsync(cancellationToken);
        var shares = await query
            .Skip((request.PageIndex - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        // Batch-load file names
        var fileIds = shares.Select(s => s.FileId).Distinct().ToList();
        var files = await _fileRepository.AsQueryable()
            .Where(f => fileIds.Contains(f.Id))
            .Select(f => new { f.Id, f.OriginalName, f.FileName })
            .ToListAsync(cancellationToken);
        var fileNameMap = files.ToDictionary(f => f.Id, f => f.OriginalName ?? f.FileName ?? string.Empty);

        var dtos = shares.Select(s => MapToShareSummary(s, fileNameMap.GetValueOrDefault(s.FileId, string.Empty))).ToList();

        IPagedList<FileShareSummaryDto> pagedList = new PagedList<FileShareSummaryDto>(dtos, request.PageIndex, request.PageSize, total);
        return Ok(pagedList);
    }

    public async Task<Result<int>> BatchRevokeSharesAsync(IEnumerable<Guid> shareIds, CancellationToken cancellationToken = default)
    {
        var idList = shareIds.ToList();
        if (idList.Count == 0)
            return Ok(0);

        var shares = await _shareRepository.AsQueryable()
            .Where(s => idList.Contains(s.Id) && s.IsEnabled)
            .ToListAsync(cancellationToken);

        foreach (var share in shares)
        {
            share.IsEnabled = false;
        }

        if (shares.Count > 0)
        {
            await _shareRepository.UpdateManyAsync(shares, cancellationToken);
        }

        LogInformation("Batch revoked {Count} shares", shares.Count);
        return Ok(shares.Count);
    }

    private static FileShareSummaryDto MapToShareSummary(FileShare share, string originalName)
    {
        return new FileShareSummaryDto
        {
            Id = share.Id,
            FileId = share.FileId,
            OriginalName = originalName,
            ShareToken = share.ShareToken,
            ExpiresAt = share.ExpiresAt,
            AccessCount = share.AccessCount,
            MaxAccessCount = share.MaxAccessCount,
            RequirePassword = share.RequirePassword,
            IsEnabled = share.IsEnabled,
            CreationTime = share.CreationTime,
            CreatorId = share.CreatorId
        };
    }

    /// <summary>
    /// 生成分享令牌（CSPRNG）
    /// </summary>
    private static string GenerateShareToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');
    }

    /// <summary>
    /// 计算密码哈希（HMAC-SHA256 + 随机盐），存储格式为 salt:hash
    /// </summary>
    private static string ComputePasswordHash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = ComputeHmacSha256(password, salt);
        return $"{Convert.ToHexString(salt)}:{Convert.ToHexString(hash)}".ToLowerInvariant();
    }

    /// <summary>
    /// 验证密码是否匹配已存储的 salt:hash
    /// </summary>
    private static bool VerifyPasswordHash(string password, string storedHash)
    {
        var parts = storedHash.Split(':');
        if (parts.Length != 2)
            return false;

        var salt = Convert.FromHexString(parts[0]);
        var expectedHash = Convert.FromHexString(parts[1]);
        var actualHash = ComputeHmacSha256(password, salt);

        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }

    private static byte[] ComputeHmacSha256(string password, byte[] salt)
    {
        using var hmac = new HMACSHA256(salt);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(password));
    }
}
