
namespace Tnzi.Identity.Services;

/// <summary>
/// 认证令牌服务实现
/// </summary>
public class AuthTokenService : ApplicationService, IAuthTokenService
{
    private readonly IRepository<AuthToken, Guid> _repository;

    /// <summary>
    /// 初始化一个<see cref="AuthTokenService"/>类型的新实例
    /// </summary>
    public AuthTokenService(IRepository<AuthToken, Guid> repository, IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
    }

    /// <summary>
    /// 保存令牌
    /// </summary>
    public async Task<Guid> SaveTokenAsync(Guid userId, string loginProvider, string name, string value, DateTime? expiresAt = null)
    {
        // 先查找是否已存在
        var existingToken = await _repository
            .Where(ut => ut.UserId == userId
                && ut.LoginProvider == loginProvider
                && ut.Name == name)
            .FirstOrDefaultAsync();

        if (existingToken != null)
        {
            // 更新现有令牌
            existingToken.Value = value;
            existingToken.ExpiresAt = expiresAt;
            existingToken.IsUsed = false;
            existingToken.UsedAt = null;
            await _repository.UpdateAsync(existingToken);
            return existingToken.Id;
        }

        // 创建新令牌
        var token = new AuthToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            LoginProvider = loginProvider,
            Name = name,
            Value = value,
            ExpiresAt = expiresAt,
            IsUsed = false,
            CreationTime = DateTime.UtcNow
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
                        && ut.Name == name)
                    .FirstOrDefaultAsync();

                if (conflictedToken != null)
                {
                    conflictedToken.Value = value;
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

        return token?.Value;
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
    /// 标记令牌为已使用（带并发控制，已被使用的令牌返回 false）
    /// </summary>
    public async Task<bool> MarkTokenAsUsedAsync(Guid tokenId)
    {
        var token = await _repository.GetAsync(tokenId);
        if (token == null || token.IsUsed)
        {
            return false;
        }

        token.IsUsed = true;
        token.UsedAt = DateTime.UtcNow;
        await _repository.UpdateAsync(token);
        return true;
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
        return await _repository
            .Where(ut => ut.LoginProvider == loginProvider
                && ut.Name == name
                && ut.Value == value
                && !ut.IsUsed
                && (ut.ExpiresAt == null || ut.ExpiresAt > DateTime.UtcNow))
            .FirstOrDefaultAsync();
    }
}