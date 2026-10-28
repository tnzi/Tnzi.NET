namespace Tnzi.Identity.Services;

/// <summary>
/// <see cref="IUserEmailProvider"/> 的 Identity 实现：账号上的 <c>Email</c> 列。
/// </summary>
/// <remarks>
/// 让不引用 Identity 的模块（Payment 等）在令牌不带邮箱声明、或根本没有当前用户（后台任务）时，
/// 仍能把账单邮件寄给发起它的那个人。
/// </remarks>
public class UserEmailProvider : IUserEmailProvider
{
    private readonly IReadOnlyRepository<User, Guid> _userRepository;

    public UserEmailProvider(IReadOnlyRepository<User, Guid> userRepository)
    {
        _userRepository = Check.NotNull(userRepository);
    }

    public async Task<IReadOnlyDictionary<Guid, string>> GetEmailsAsync(
        IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default)
    {
        if (userIds == null || userIds.Count == 0)
            return new Dictionary<Guid, string>();

        var ids = userIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (ids.Count == 0)
            return new Dictionary<Guid, string>();

        var users = await _userRepository.AsNoTracking()
            .Where(u => ids.Contains(u.Id) && u.Email != null && u.Email != "")
            .Select(u => new { u.Id, u.Email })
            .ToListAsync(cancellationToken);

        return users.ToDictionary(u => u.Id, u => u.Email!);
    }
}
