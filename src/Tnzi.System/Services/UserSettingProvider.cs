namespace Tnzi.System.Services;

/// <summary>
/// 用户级配置提供者（优先级最高）
/// </summary>
public class UserSettingProvider : ISettingProvider
{
    private readonly IRepository<Setting, Guid> _repository;
    private readonly ICurrentUser? _currentUser;
    private readonly ISettingEncryptor? _settingEncryptor;
    private readonly ILogger<UserSettingProvider> _logger;

    public string Name => "User";
    public int Priority => 300;

    public UserSettingProvider(
        IRepository<Setting, Guid> repository,
        ILogger<UserSettingProvider> logger,
        ICurrentUser? currentUser = null,
        ISettingEncryptor? settingEncryptor = null)
    {
        _repository = Check.NotNull(repository);
        _logger = Check.NotNull(logger);
        _currentUser = currentUser;
        _settingEncryptor = settingEncryptor;
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

        if (setting == null)
            return null;

        if (setting.IsEncrypted)
        {
            if (_settingEncryptor == null)
            {
                _logger.LogWarning("Encrypted setting '{Key}' found but encryption is not enabled, returning null for safety", key);
                return null;
            }

            return setting.Value != null ? _settingEncryptor.Decrypt(setting.Value) : null;
        }

        return setting.Value;
    }
}
