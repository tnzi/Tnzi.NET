namespace Tnzi.Storage.Workspace.Services;

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

    public async Task<Result<FileSharePublicDto>> CreateShareAsync(Guid fileId, DateTime? expiresAt = null, int? maxAccessCount = null, string? password = null, CancellationToken cancellationToken = default)
    {
        var fileRecord = await _fileRepository.GetAsync(fileId, cancellationToken);
        if (fileRecord == null)
            return Fail<FileSharePublicDto>("File not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);

        // A share link is a bearer credential for the file's bytes. Minting one
        // for a file you may not even read would hand that credential out, so
        // this needs write-level rights, not merely the file's id.
        if (!await _accessAuthorizer.CanWriteAsync(fileRecord, cancellationToken))
            return Fail<FileSharePublicDto>("File not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);

        // 部署级策略:管理员开了强制口令,就没有"这条我不设"的余地。
        var options = ShareOptions;
        if (options.RequirePassword && string.IsNullOrEmpty(password))
        {
            return Fail<FileSharePublicDto>(
                "This deployment requires every share link to have a password", 400, ErrorCodes.VALIDATION_ERROR);
        }

        // 生成唯一的分享令牌
        var shareToken = GenerateShareToken();

        // 计算密码哈希（如果需要）。PBKDF2 慢哈希，格式带版本前缀（见 SharePasswordHasher）。
        string? passwordHash = null;
        if (!string.IsNullOrEmpty(password))
        {
            passwordHash = SharePasswordHasher.Hash(password);
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
        return Ok(MapToPublicDto(share), "File share created successfully");
    }

    public async Task<Result<FileSharePublicDto>> GetShareAsync(string shareToken, CancellationToken cancellationToken = default)
    {
        var share = await _shareRepository.FindAsync((FileShare s) => s.ShareToken == shareToken, cancellationToken);

        // 管理视角带 FileId 与计数，且不论链接活着还是已撤销都如实返回 —— 所以它只给管理者看。
        // 拒绝与"令牌不存在"同一句：区分开就等于告诉试探者这个令牌是真的。
        if (share == null || !await CanViewShareAsync(share, cancellationToken))
            return NotFoundShare<FileSharePublicDto>();

        return Ok(MapToPublicDto(share));
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

    /// <summary>
    /// 管理视角能不能看：管理者（见 <see cref="CanManageShareAsync"/>），或者<b>本次请求已凭这条链接通过校验</b>。
    /// </summary>
    /// <remarks>
    /// 后一条服务的是下载流程 <c>Validate → GetShare → 取字节</c>：收件人没有账号，既不是创建者也写不了文件，
    /// 但校验通过那一刻 <see cref="ValidateShareAccessAsync"/> 已把该文件写进请求作用域的授予表 ——
    /// 控制器随后凭 GetShare 拿 FileId。授予是请求级凭据，跨请求不成立，所以别的登录用户凭令牌
    /// 单独调 GetShare 仍是 404。
    /// </remarks>
    private async Task<bool> CanViewShareAsync(FileShare share, CancellationToken cancellationToken)
        => _grantContext.IsGranted(share.FileId) || await CanManageShareAsync(share, cancellationToken);

    /// <summary>
    /// 谁能<b>管理</b>一条分享（撤销 / 看令牌与计数）：与创建它所要求的是同一份权利。
    /// </summary>
    /// <remarks>
    /// <list type="number">
    /// <item>创建者本人 —— 即便他后来失去了对文件的变更权，他发出去的链接仍归他收回。</item>
    /// <item>对文件有变更权的人（<see cref="IFileAccessAuthorizer.CanWriteAsync"/>：所有者或持 <c>storage.file.update</c>），
    /// 这正是 <see cref="CreateShareAsync"/> 要求的那份权利。</item>
    /// <item>文件已删而分享行还在时授权器没有 <c>FileRecord</c> 可问，退回权限码：只有持 <c>storage.file.update</c> 的管理员能收尾。</item>
    /// </list>
    /// 请求级授予（分享令牌 / 签署令牌）<b>刻意不算</b>：它证明的是"这一次请求可以读这个文件"，不是"你是这条链接的主人"。
    /// </remarks>
    private async Task<bool> CanManageShareAsync(FileShare share, CancellationToken cancellationToken)
    {
        if (IsShareCreator(share))
            return true;

        var file = await _fileRepository.GetAsync(share.FileId, cancellationToken);
        if (file != null)
            return await _accessAuthorizer.CanWriteAsync(file, cancellationToken);

        return PermissionChecker != null
               && await PermissionChecker.IsGrantedAsync(StoragePermissionNames.FileUpdate);
    }

    /// <summary>
    /// 归属判定，与 <c>FileAccessAuthorizer.IsOwner</c> 同构：<c>CreatorId</c> 为 null 的行（后台任务产生）不属于任何人。
    /// </summary>
    private bool IsShareCreator(FileShare share)
        => share.CreatorId.HasValue
           && CurrentUser?.Id is { } me
           && share.CreatorId.Value == me;

    public async Task<Result> RevokeShareAsync(string shareToken, CancellationToken cancellationToken = default)
    {
        var share = await _shareRepository.FindAsync((FileShare s) => s.ShareToken == shareToken, cancellationToken);

        // 撤销要求与创建同一份权利。此前这里一个判据都没有：任何已登录用户拿到一个泄漏的令牌，
        // 就能把别人发出去的链接撤掉，而创建者毫不知情。
        if (share == null || !await CanManageShareAsync(share, cancellationToken))
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
            var needsRehash = false;
            if (!supplied || !SharePasswordHasher.Verify(password!, share.PasswordHash!, out needsRehash))
            {
                await RecordFailedAttemptAsync(share, options, cancellationToken);
                return Ok(false);
            }

            // 连续失败计数只在真正通过时清零 —— 否则攻击者只要偶尔混进一次正确请求
            // 就能把闸门重置。这里"通过"就是唯一的重置条件。
            // ★ 同一次写入顺带把旧格式（单轮 HMAC）的哈希就地升级成 PBKDF2：
            //   只有此刻手里有明文口令，才算得出新哈希；存量链接因此不需要重发。
            if (share.FailedAttemptCount > 0 || needsRehash)
            {
                var writable = await LoadForUpdateAsync(share, cancellationToken);
                writable.FailedAttemptCount = 0;
                if (needsRehash)
                    writable.PasswordHash = SharePasswordHasher.Hash(password!);
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

    /// <summary>
    /// 投影为对外 DTO。<b>绝不含 PasswordHash</b> —— 这层投影此前在控制器里，
    /// 契约改成 DTO 之后收进服务，控制器边界从此没有机会漏出实体字段。
    /// </summary>
    private static FileSharePublicDto MapToPublicDto(FileShare share)
    {
        return new FileSharePublicDto
        {
            Id = share.Id,
            FileId = share.FileId,
            ShareToken = share.ShareToken,
            ExpiresAt = share.ExpiresAt,
            MaxAccessCount = share.MaxAccessCount,
            AccessCount = share.AccessCount,
            RequirePassword = share.RequirePassword,
            IsEnabled = share.IsEnabled,
            CreationTime = share.CreationTime
        };
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
}
