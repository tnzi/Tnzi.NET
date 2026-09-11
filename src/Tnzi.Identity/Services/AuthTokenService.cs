
namespace Tnzi.Identity.Services;

/// <summary>
/// 认证令牌服务实现
/// </summary>
public class AuthTokenService : ApplicationService, IAuthTokenService
{
    private readonly IRepository<AuthToken, Guid> _repository;
    private readonly IDataProtector _protector;

    /// <summary>
    /// 初始化一个<see cref="AuthTokenService"/>类型的新实例
    /// </summary>
    /// <remarks>
    /// ★ <see cref="IDataProtectionProvider"/> 由 <c>IdentityModule</c> 注册
    /// （<c>services.AddDataProtection()</c>）。令牌值以密文落库、以哈希查找 ——
    /// 理由写在 <see cref="AuthToken.Value"/> 与 <see cref="AuthToken.ValueHash"/> 上。
    /// </remarks>
    public AuthTokenService(
        IRepository<AuthToken, Guid> repository,
        IDataProtectionProvider dataProtectionProvider,
        IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
        _protector = Check.NotNull(dataProtectionProvider).CreateProtector(AuthToken.ValueProtectorPurpose);
    }

    /// <summary>把令牌值加密成落库形态。</summary>
    private string Protect(string value) => _protector.Protect(value);

    /// <summary>
    /// 把落库的密文还原成令牌值；解不开时返回 <c>null</c>。
    /// </summary>
    /// <remarks>
    /// ★★ <strong>解不开必须当作「没有这枚令牌」，不能抛。</strong>
    /// key ring 轮换或丢失时 <c>Unprotect</c> 会抛 <see cref="CryptographicException"/>，
    /// 而这条链路上的调用方（刷新、读邀请预填资料）都在处理一个普通请求 ——
    /// 让它变成 500 只会把一次「令牌失效，请重新登录」变成一次看不懂的服务器错误。
    /// </remarks>
    private string? Unprotect(string? cipherText)
    {
        if (string.IsNullOrEmpty(cipherText))
        {
            return null;
        }

        try
        {
            return _protector.Unprotect(cipherText);
        }
        catch (CryptographicException ex)
        {
            LogWarning("An auth token could not be decrypted ({Reason}); treating it as absent.", ex.Message);
            return null;
        }
    }

    /// <summary>
    /// 保存令牌
    /// </summary>
    public async Task<Guid> SaveTokenAsync(Guid userId, string loginProvider, string name, string value, DateTime? expiresAt = null, Guid sessionId = default)
    {
        // 先查找是否已存在（唯一键含 SessionId：会话绑定令牌按会话各存一条）
        var existingToken = await _repository
            .Where(ut => ut.UserId == userId
                && ut.LoginProvider == loginProvider
                && ut.Name == name
                && ut.SessionId == sessionId)
            .FirstOrDefaultAsync();

        // 两列一起写：密文供读回，哈希供等值查找。分开写会出现「查得到但解不开」
        // 或者反过来的半截状态，而两者都表现为「令牌莫名其妙失效」。
        var cipherText = Protect(value);
        var valueHash = OneTimeToken.Hash(value);

        if (existingToken != null)
        {
            // 更新现有令牌
            existingToken.Value = cipherText;
            existingToken.ValueHash = valueHash;
            existingToken.ExpiresAt = expiresAt;
            existingToken.IsUsed = false;
            existingToken.UsedAt = null;
            await _repository.UpdateAsync(existingToken);
            return existingToken.Id;
        }

        // 创建新令牌（Id 和 CreationTime 由框架自动生成）
        var token = new AuthToken
        {
            UserId = userId,
            LoginProvider = loginProvider,
            Name = name,
            Value = cipherText,
            ValueHash = valueHash,
            ExpiresAt = expiresAt,
            SessionId = sessionId,
            IsUsed = false
        };

        try
        {
            await _repository.InsertAsync(token);
            return token.Id;
        }
        catch (DbUpdateException ex)
        {
            // 处理并发插入时的唯一约束冲突
            // 如果两个请求同时检测到不存在并尝试插入，数据库唯一约束会阻止第二个插入
            if (ex.IsUniqueConstraintViolation())
            {
                // 并发插入冲突，重新查询并更新
                var conflictedToken = await _repository
                    .Where(ut => ut.UserId == userId
                        && ut.LoginProvider == loginProvider
                        && ut.Name == name
                        && ut.SessionId == sessionId)
                    .FirstOrDefaultAsync();

                if (conflictedToken != null)
                {
                    conflictedToken.Value = cipherText;
                    conflictedToken.ValueHash = valueHash;
                    conflictedToken.ExpiresAt = expiresAt;
                    conflictedToken.IsUsed = false;
                    conflictedToken.UsedAt = null;
                    await _repository.UpdateAsync(conflictedToken);
                    return conflictedToken.Id;
                }
            }
            // 其他数据库错误，重新抛出
            throw;
        }
    }

    /// <summary>
    /// 获取令牌
    /// </summary>
    public async Task<string?> GetTokenAsync(Guid userId, string loginProvider, string name)
    {
        var token = await _repository
            .Where(ut => ut.UserId == userId
                && ut.LoginProvider == loginProvider
                && ut.Name == name
                && !ut.IsUsed
                && (ut.ExpiresAt == null || ut.ExpiresAt > DateTime.UtcNow))
            .FirstOrDefaultAsync();

        return Unprotect(token?.Value);
    }

    /// <summary>
    /// 删除令牌
    /// </summary>
    public async Task RemoveTokenAsync(Guid userId, string loginProvider, string name)
    {
        var token = await _repository
            .Where(ut => ut.UserId == userId
                && ut.LoginProvider == loginProvider
                && ut.Name == name)
            .FirstOrDefaultAsync();

        if (token != null)
        {
            await _repository.DeleteAsync(token);
        }
    }

    /// <summary>
    /// 删除用户的所有令牌
    /// </summary>
    public async Task RemoveAllTokensAsync(Guid userId, string? loginProvider = null)
    {
        await ExecuteInUnitOfWorkAsync(async cancellationToken =>
        {
            var query = _repository.Where(ut => ut.UserId == userId);

            if (!string.IsNullOrEmpty(loginProvider))
            {
                query = query.Where(ut => ut.LoginProvider == loginProvider);
            }

            var tokens = await query.ToListAsync(cancellationToken);
            if (tokens.Count > 0)
            {
                await _repository.DeleteManyAsync(tokens);
            }
        });
    }

    /// <summary>
    /// 标记令牌为已使用（原子操作，防止并发重放攻击）
    /// </summary>
    public async Task<bool> MarkTokenAsUsedAsync(Guid tokenId)
    {
        // 使用原子更新：WHERE Id = @id AND IsUsed = false，防止 TOCTOU 竞态条件
        var affectedRows = await _repository
            .Where(t => t.Id == tokenId && !t.IsUsed)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(t => t.IsUsed, true)
                .SetProperty(t => t.UsedAt, DateTime.UtcNow));

        return affectedRows > 0;
    }

    /// <summary>
    /// 清理过期的令牌
    /// </summary>
    public async Task<int> CleanExpiredTokensAsync()
    {
        return await ExecuteInUnitOfWorkAsync(async cancellationToken =>
        {
            var now = DateTime.UtcNow;
            var expiredTokens = await _repository
                .Where(ut => ut.ExpiresAt != null && ut.ExpiresAt < now)
                .ToListAsync(cancellationToken);

            if (expiredTokens.Count > 0)
            {
                await _repository.DeleteManyAsync(expiredTokens);
            }

            return expiredTokens.Count;
        });
    }

    /// <summary>
    /// 获取用户的所有令牌
    /// </summary>
    public async Task<IEnumerable<AuthToken>> GetUserTokensAsync(Guid userId, string? loginProvider = null)
    {
        var query = _repository.Where(ut => ut.UserId == userId);

        if (!string.IsNullOrEmpty(loginProvider))
        {
            query = query.Where(ut => ut.LoginProvider == loginProvider);
        }

        return await query.OrderByDescending(ut => ut.CreationTime).ToListAsync();
    }

    /// <summary>
    /// 通过令牌值查找令牌（用于临时Token查找用户）
    /// </summary>
    public async Task<AuthToken?> FindTokenByValueAsync(string loginProvider, string name, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        // ★ 按哈希查，不按值查：Value 现在是密文，而密文每次加密结果不同，等值比较必然落空。
        //   哈希是确定性的、定宽的、有索引的 —— 这是三件事同时成立的那一列。
        var hash = OneTimeToken.Hash(value);

        return await _repository
            .Where(ut => ut.LoginProvider == loginProvider
                && ut.Name == name
                && ut.ValueHash == hash
                && !ut.IsUsed
                && (ut.ExpiresAt == null || ut.ExpiresAt > DateTime.UtcNow))
            .FirstOrDefaultAsync();
    }

    /// <inheritdoc />
    public string? RevealTokenValue(AuthToken token)
    {
        Check.NotNull(token);
        return Unprotect(token.Value);
    }

    /// <inheritdoc />
    public async Task<AuthToken?> FindTokenByPreviousValueAsync(string loginProvider, string name, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var hash = OneTimeToken.Hash(value);

        return await _repository
            .Where(ut => ut.LoginProvider == loginProvider
                && ut.Name == name
                && ut.PreviousValueHash == hash)
            .FirstOrDefaultAsync();
    }

    /// <inheritdoc />
    public async Task<bool> RotateRefreshTokenAsync(Guid tokenId, string expectedCurrentValue, string newValue, DateTime? expiresAt)
    {
        Check.NotNullOrWhiteSpace(expectedCurrentValue);
        Check.NotNullOrWhiteSpace(newValue);

        var previousHash = OneTimeToken.Hash(expectedCurrentValue);
        var newCipherText = Protect(newValue);
        var newHash = OneTimeToken.Hash(newValue);
        var now = DateTime.UtcNow;

        // 条件更新即抢占：值还是调用方读到的那个，才轮换得动。
        // ★ 比较条件从 Value 换成 ValueHash —— 密文不可等值比较，而这里的 CAS 语义
        //   （「这一行还是我读到的那一代吗」）由哈希表达得同样精确。
        var affectedRows = await _repository
            .Where(t => t.Id == tokenId && t.ValueHash == previousHash)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(t => t.Value, newCipherText)
                .SetProperty(t => t.ValueHash, newHash)
                .SetProperty(t => t.PreviousValueHash, previousHash)
                .SetProperty(t => t.RotatedAt, now)
                .SetProperty(t => t.ExpiresAt, expiresAt)
                .SetProperty(t => t.IsUsed, false)
                .SetProperty(t => t.UsedAt, (DateTime?)null));

        return affectedRows > 0;
    }

    /// <inheritdoc />
    public async Task<int> RemoveSessionTokensAsync(IReadOnlyCollection<Guid> sessionIds)
    {
        Check.NotNull(sessionIds);

        // Guid.Empty 表示「不绑定任何会话」（2FA 临时令牌、passkey 注册令牌）。
        // 把它当成一个会话ID去删，会把该用户所有待完成流程的令牌一起清掉 ——
        // 而调用方要撤的是登录会话，不是这些。
        var targets = sessionIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (targets.Count == 0)
        {
            return 0;
        }

        return await _repository
            .Where(t => targets.Contains(t.SessionId))
            .ExecuteDeleteAsync();
    }

    /// <inheritdoc />
    public async Task<int> RemoveUserSessionTokensAsync(Guid userId, Guid? excludeSessionId = null)
    {
        var exclude = excludeSessionId ?? Guid.Empty;

        return await _repository
            .Where(t => t.UserId == userId
                && t.SessionId != Guid.Empty
                && t.SessionId != exclude)
            .ExecuteDeleteAsync();
    }
}
