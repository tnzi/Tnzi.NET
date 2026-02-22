
namespace Tnzi.Identity.Services;

/// <summary>
/// 用户登录记录服务实现
/// </summary>
public class UserLoginService : ApplicationService, IUserLoginService
{
    private readonly DbContext _dbContext;

    /// <summary>
    /// 初始化一个<see cref="UserLoginService"/>类型的新实例
    /// </summary>
    public UserLoginService(DbContext dbContext, IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        _dbContext = Check.NotNull(dbContext);
    }

    /// <summary>
    /// 记录用户登录
    /// </summary>
    public async Task RecordLoginAsync(Guid userId, string loginProvider, string providerKey, string? displayName = null)
    {
        // 检查是否已存在
        var existing = await _dbContext.Set<UserLogin>()
            .FirstOrDefaultAsync(ul => ul.UserId == userId
                && ul.LoginProvider == loginProvider
                && ul.ProviderKey == providerKey);

        if (existing != null)
        {
            // 更新显示名称
            existing.ProviderDisplayName = displayName;
            _dbContext.Set<UserLogin>().Update(existing);
        }
        else
        {
            // 创建新记录
            var userLogin = new UserLogin
            {
                UserId = userId,
                LoginProvider = loginProvider,
                ProviderKey = providerKey,
                ProviderDisplayName = displayName
            };
            await _dbContext.Set<UserLogin>().AddAsync(userLogin);
        }

        await _dbContext.SaveChangesAsync();
    }

    /// <summary>
    /// 获取用户的登录记录
    /// </summary>
    public async Task<Result<IEnumerable<UserLoginDto>>> GetUserLoginsAsync(Guid userId)
    {
        var logins = await _dbContext.Set<UserLogin>()
            .Where(ul => ul.UserId == userId)
            .ToListAsync();

        var dtos = logins.Select(ul => new UserLoginDto
        {
            UserId = ul.UserId,
            LoginProvider = ul.LoginProvider,
            ProviderKey = ul.ProviderKey,
            ProviderDisplayName = ul.ProviderDisplayName
        });

        return Ok<IEnumerable<UserLoginDto>>(dtos);
    }

    /// <summary>
    /// 删除用户登录记录
    /// </summary>
    public async Task RemoveLoginAsync(Guid userId, string loginProvider, string providerKey)
    {
        var login = await _dbContext.Set<UserLogin>()
            .FirstOrDefaultAsync(ul => ul.UserId == userId
                && ul.LoginProvider == loginProvider
                && ul.ProviderKey == providerKey);

        if (login != null)
        {
            _dbContext.Set<UserLogin>().Remove(login);
            await _dbContext.SaveChangesAsync();
        }
    }

    /// <summary>
    /// 检查用户是否已绑定指定登录提供者
    /// </summary>
    public async Task<bool> HasLoginAsync(Guid userId, string loginProvider, string providerKey)
    {
        return await _dbContext.Set<UserLogin>()
            .AnyAsync(ul => ul.UserId == userId
                && ul.LoginProvider == loginProvider
                && ul.ProviderKey == providerKey);
    }
}

