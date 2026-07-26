namespace Tnzi.Identity.Services;

/// <summary>
/// <see cref="IUserDisplayNameProvider"/> 的 Identity 实现
/// </summary>
/// <remarks>
/// 让不引用 Identity 的模块（Finance 等）也能把 <c>CreatorId</c> 显示成名字。
/// 名字取"人愿意被怎么称呼"的优先级：昵称 → 全名 → 用户名，与登录状态栏、
/// 聊天窗等既有称呼点一致，避免同一个人在两处显示成两个名字。
/// </remarks>
public class UserDisplayNameProvider : IUserDisplayNameProvider
{
    private readonly IReadOnlyRepository<User, Guid> _userRepository;
    private readonly IReadOnlyRepository<UserDetail, Guid> _detailRepository;

    public UserDisplayNameProvider(
        IReadOnlyRepository<User, Guid> userRepository,
        IReadOnlyRepository<UserDetail, Guid> detailRepository)
    {
        _userRepository = Check.NotNull(userRepository);
        _detailRepository = Check.NotNull(detailRepository);
    }

    public async Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesAsync(
        IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default)
    {
        if (userIds == null || userIds.Count == 0)
            return new Dictionary<Guid, string>();

        var ids = userIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (ids.Count == 0)
            return new Dictionary<Guid, string>();

        var users = await _userRepository.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.UserName })
            .ToListAsync(cancellationToken);

        // 名字在 UserDetail 而不是 User 上；两张表各取一次，不做 N+1。
        var details = await _detailRepository.AsNoTracking()
            .Where(d => ids.Contains(d.UserId))
            .Select(d => new { d.UserId, d.Nickname, d.FirstName, d.LastName })
            .ToListAsync(cancellationToken);
        var detailByUser = details.ToDictionary(d => d.UserId);

        var result = new Dictionary<Guid, string>(users.Count);
        foreach (var user in users)
        {
            detailByUser.TryGetValue(user.Id, out var detail);
            var fullName = $"{detail?.FirstName} {detail?.LastName}".Trim();
            var name = !string.IsNullOrWhiteSpace(detail?.Nickname) ? detail!.Nickname
                : !string.IsNullOrWhiteSpace(fullName) ? fullName
                : user.UserName;

            // 解析不到名字的行不放进结果：调用方按缺失处理，不必区分"查不到"与"空名字"。
            if (!string.IsNullOrWhiteSpace(name))
                result[user.Id] = name!;
        }

        return result;
    }
}
