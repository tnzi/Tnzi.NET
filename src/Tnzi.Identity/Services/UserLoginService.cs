
namespace Tnzi.Identity.Services;

/// <summary>
/// 用户登录记录服务实现：第三方登录关联的只读查询。
/// </summary>
/// <remarks>
/// 关联与解除关联经 <c>UserManager.AddLoginAsync</c> / <c>RemoveLoginAsync</c> 写入，它们写的就是这里读的那张表。
/// </remarks>
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

