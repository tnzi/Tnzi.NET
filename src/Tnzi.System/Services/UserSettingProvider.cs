namespace Tnzi.System.Services;

/// <summary>
/// 用户级配置提供者（优先级最高）
/// </summary>
public class UserSettingProvider : ISettingProvider
{
    private readonly IRepository<Setting, Guid> _repository;
    private readonly ICurrentUser? _currentUser;

    public string Name => "User";
    public int Priority => 300;

    public UserSettingProvider(
        IRepository<Setting, Guid> repository,
        ICurrentUser? currentUser = null)
    {
        _repository = Check.NotNull(repository);
        _currentUser = currentUser;
    }

    public async Task<string?> GetOrNullAsync(string key, CancellationToken cancellationToken = default)
    {
        var userId = _currentUser?.Id;
        if (userId == null) return null;

        var scopeId = userId.Value.ToString();
        var setting = await _repository.AsQueryable()
            .AsNoTracking()
            .Where(s => s.Key == key && s.Scope == SettingScope.User && s.ScopeId == scopeId)
            .FirstOrDefaultAsync(cancellationToken);

        return setting?.Value;
    }
}
