namespace Tnzi.Storage.Services;

/// <summary>
/// 文件分享服务实现
/// </summary>
public class FileShareService : ApplicationService, IFileShareService
{
    private readonly IRepository<FileShare, Guid> _shareRepository;
    private readonly IRepository<FileRecord, Guid> _fileRepository;
    private readonly IFileAccessAuthorizer _accessAuthorizer;
    private readonly IFileAccessGrantContext _grantContext;
    private readonly IOptionsMonitor<StorageOptions> _optionsMonitor;

    private ShareOptions ShareOptions => _optionsMonitor.CurrentValue.Share;

    public FileShareService(
        IRepository<FileShare, Guid> shareRepository,
        IRepository<FileRecord, Guid> fileRepository,
        IFileAccessAuthorizer accessAuthorizer,
        IFileAccessGrantContext grantContext,
        IOptionsMonitor<StorageOptions> optionsMonitor,
        IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        _shareRepository = Check.NotNull(shareRepository);
        _fileRepository = Check.NotNull(fileRepository);
        _accessAuthorizer = Check.NotNull(accessAuthorizer);
        _grantContext = Check.NotNull(grantContext);
        _optionsMonitor = Check.NotNull(optionsMonitor);
    }

    public async Task<Result<FileShare>> CreateShareAsync(Guid fileId, DateTime? expiresAt = null, int? maxAccessCount = null, string? password = null, CancellationToken cancellationToken = default)
    {
        var fileRecord = await _fileRepository.GetAsync(fileId, cancellationToken);
        if (fileRecord == null)
            return Fail<FileShare>("File not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);

        // A share link is a bearer credential for the file's bytes. Minting one
        // for a file you may not even read would hand that credential out, so
        // this needs write-level rights, not merely the file's id.
        if (!await _accessAuthorizer.CanWriteAsync(fileRecord, cancellationToken))
            return Fail<FileShare>("File not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);

        // 部署级策略:管理员开了强制口令,就没有"这条我不设"的余地。
        var options = ShareOptions;
        if (options.RequirePassword && string.IsNullOrEmpty(password))
        {
            return Fail<FileShare>(
                "This deployment requires every share link to have a password", 400, ErrorCodes.VALIDATION_ERROR);
        }

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
            ExpiresAt = ResolveExpiry(expiresAt, options),
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

    public async Task<Result<FileSharePreviewDto>> GetSharePreviewAsync(string shareToken, CancellationToken cancellationToken = default)
    {
        var options = ShareOptions;
        if (!options.AllowAnonymous && !(CurrentUser?.IsAuthenticated ?? false))
            return NotFoundShare<FileSharePreviewDto>();

        var share = await _shareRepository.FindAsync((FileShare s) => s.ShareToken == shareToken, cancellationToken);

        // 撤销 / 过期 / 次数用尽全部折叠成同一个 404,与"令牌不存在"无法区分。
        if (share == null
            || !share.IsEnabled
            || (share.ExpiresAt.HasValue && share.ExpiresAt.Value < DateTime.UtcNow)
            || (share.MaxAccessCount.HasValue && share.AccessCount >= share.MaxAccessCount.Value))
        {
            return NotFoundShare<FileSharePreviewDto>();
        }

        // 文件本身被删掉了,链接就没有意义 —— 同样 404,不解释。
        var file = await _fileRepository.GetAsync(share.FileId, cancellationToken);
        if (file == null)
            return NotFoundShare<FileSharePreviewDto>();

        return Ok(new FileSharePreviewDto
        {
            FileName = file.OriginalName ?? file.FileName,
            Size = file.Size,
            ContentType = file.ContentType,
            RequirePassword = share.RequirePassword,
            ExpiresAt = share.ExpiresAt
        });
    }

    /// <summary>所有"这条链接用不了"的原因共用同一个回答。</summary>
    private Result<T> NotFoundShare<T>() => Fail<T>("Share not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);

    public async Task<Result> RevokeShareAsync(string shareToken, CancellationToken cancellationToken = default)
    {
        var share = await _shareRepository.FindAsync((FileShare s) => s.ShareToken == shareToken, cancellationToken);
        if (share == null)
            return Fail("Share not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);

        var writable = await LoadForUpdateAsync(share, cancellationToken);
        writable.IsEnabled = false;
        await _shareRepository.UpdateAsync(writable, cancellationToken);
        LogInformation("File share revoked: ShareToken: {ShareToken}", shareToken);
        return Ok("File share revoked successfully");
    }

    /// <summary>
    /// 校验一条分享链接是否可用。**通过时把该文件记进请求作用域的授予表**
    /// （<see cref="IFileAccessGrantContext"/>），后续的取记录 / 取流因此不再要求调用者
    /// 本人有权 —— 分享链接的凭据是令牌本身,收件人往往根本没有账号。
    ///
    /// 所有拒绝都返回同一个 <c>false</c>,不区分"令牌不存在 / 已过期 / 次数用尽 / 口令错" ——
    /// 区分开就等于告诉试探者"这个令牌是真的,只是口令不对"。
    /// </summary>
    public async Task<Result<bool>> ValidateShareAccessAsync(string shareToken, string? password = null, CancellationToken cancellationToken = default)
    {
        var options = ShareOptions;

        // 部署把匿名分享关掉时,链接退化成"内部传阅":仍然有效,但只对已登录用户。
        if (!options.AllowAnonymous && !(CurrentUser?.IsAuthenticated ?? false))
            return Ok(false);

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
            var supplied = !string.IsNullOrEmpty(password) && !string.IsNullOrEmpty(share.PasswordHash);
            if (!supplied || !VerifyPasswordHash(password!, share.PasswordHash!))
            {
                await RecordFailedAttemptAsync(share, options, cancellationToken);
                return Ok(false);
            }

            // 连续失败计数只在真正通过时清零 —— 否则攻击者只要偶尔混进一次正确请求
            // 就能把闸门重置。这里"通过"就是唯一的重置条件。
            if (share.FailedAttemptCount > 0)
            {
                var writable = await LoadForUpdateAsync(share, cancellationToken);
                writable.FailedAttemptCount = 0;
                await _shareRepository.UpdateAsync(writable, cancellationToken);
            }
        }

        _grantContext.Grant(share.FileId);
        return Ok(true);
    }

    /// <summary>
    /// 记一次口令输错;达到上限就把链接停用。
    ///
    /// 令牌是 256 位随机数猜不到,但口令可以在线爆破。停用而不是"锁 N 分钟":
    /// 分享链接是一次性的对外物件,有人在爆破就说明它已经泄漏,让创建者重发一条
    /// 比让它自动解锁更合理。
    ///
    /// ★**必须逃出外层事务**:输错口令的请求以失败响应收场(401),而启用了
    /// `EnableGlobalUnitOfWork` 的部署会把失败请求的整个事务回滚 —— 计数于是永远
    /// 写不进去,这道闸门就形同虚设(实测正是如此:连错 10 次后正确口令照样放行)。
    /// 所以在**独立 DI 作用域**里落库,与 Identity 保存 2FA 临时令牌是同一条路子。
    /// </summary>
    private async Task RecordFailedAttemptAsync(FileShare share, ShareOptions options, CancellationToken cancellationToken)
    {
        if (options.MaxFailedPasswordAttempts <= 0)
            return;

        // 拿不到作用域工厂(未接 DI 的宿主 / 单元测试)时退回常规保存:
        // 未启用全局 UoW 的部署仍能工作,只是逃不出事务。
        var scopeFactory = ServiceProvider?.GetService<IServiceScopeFactory>();
        if (scopeFactory == null)
        {
            var inline = await LoadForUpdateAsync(share, cancellationToken);
            ApplyFailedAttempt(inline, options);
            await _shareRepository.UpdateAsync(inline, cancellationToken);
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetService<IRepository<FileShare, Guid>>();
        if (repository == null)
            return;

        var writable = await repository.GetAsync(share.Id, cancellationToken);
        if (writable == null)
            return;

        ApplyFailedAttempt(writable, options);
        await repository.UpdateAsync(writable, cancellationToken);
    }

    private void ApplyFailedAttempt(FileShare share, ShareOptions options)
    {
        share.FailedAttemptCount++;
        if (share.FailedAttemptCount >= options.MaxFailedPasswordAttempts)
        {
            share.IsEnabled = false;
            LogWarning("File share disabled after {Count} failed password attempts: ShareToken: {ShareToken}",
                share.FailedAttemptCount, share.ShareToken);
        }
    }

    /// <summary>
    /// 取回**可写**的那一份。
    ///
    /// 按令牌查用的是 <c>FindAsync(predicate)</c>，它显式走 AsNoTracking（仓储把 Find
    /// 当只读查询）；把那个实例直接交给 <c>UpdateAsync</c> 会 Attach 出
    /// "another instance with the same key is already being tracked" —— 同一请求里刚
    /// 创建过这条分享时必现。按 id 再取一次会命中变更跟踪器里已有的那个实例。
    ///
    /// 仓储给不出时（单元测试里只 stub 了按谓词查的 mock）退回原实例，行为不变。
    /// </summary>
    private async Task<FileShare> LoadForUpdateAsync(FileShare share, CancellationToken cancellationToken)
        => await _shareRepository.GetAsync(share.Id, cancellationToken) ?? share;

    /// <summary>
    /// 套用部署级的有效期策略:没选就给默认值,选得太远就收窄到上限。
    ///
    /// 超限**收窄而不是报错**:创建分享的人多半只是随手挑了个远日期,为此让他重试一遍
    /// 没有意义。默认给一个有限期限则是因为**永不过期的链接没有人会记得回来撤销**。
    /// </summary>
    private static DateTime? ResolveExpiry(DateTime? requested, ShareOptions options)
    {
        var ceiling = options.MaxExpiryDays > 0
            ? DateTime.UtcNow.AddDays(options.MaxExpiryDays)
            : (DateTime?)null;

        if (requested is null)
        {
            return options.DefaultExpiryDays > 0 ? DateTime.UtcNow.AddDays(options.DefaultExpiryDays) : ceiling;
        }

        return ceiling.HasValue && requested.Value > ceiling.Value ? ceiling : requested;
    }

    public async Task<Result<bool>> IncrementShareAccessCountAsync(string shareToken, CancellationToken cancellationToken = default)
    {
        // 原子 check-and-increment：单条 SQL 同时校验"启用 + 未超过 MaxAccessCount"并自增，
        // WHERE 条件确保仅在仍有配额时才更新；受影响行数 > 0 表示成功占用一次配额。
        var affectedRows = await _shareRepository.AsQueryable(withTracking: false)
            .Where(s => s.ShareToken == shareToken
                && s.IsEnabled
                && (s.MaxAccessCount == null || s.AccessCount < s.MaxAccessCount))
            .ExecuteUpdateAsync(set => set
                .SetProperty(x => x.AccessCount, x => x.AccessCount + 1)
                .SetProperty(x => x.LastAccessedAt, _ => DateTime.UtcNow), cancellationToken);

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
            CreatorId = share.CreatorId,
            LastAccessedAt = share.LastAccessedAt
        };
    }

    /// <summary>
    /// 生成分享令牌（CSPRNG）
    /// </summary>
    private static string GenerateShareToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        // 无填充的 URL 安全编码（令牌要进 URL 路径段）。BCL 的 Base64Url 与此前手写的
        // ToBase64String + 字符替换 + TrimEnd('=') 完全等价，既有库中令牌不受影响。
        return Base64Url.EncodeToString(bytes);
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
