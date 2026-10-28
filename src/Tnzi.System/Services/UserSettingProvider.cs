namespace Tnzi.System.Services;

/// <summary>
/// 用户级配置提供者（优先级最高）
/// </summary>
public class UserSettingProvider : ISettingProvider
{
    private readonly IRepository<Setting, Guid> _repository;
    private readonly ICurrentUser? _currentUser;
    private readonly ISettingEncryptor? _settingEncryptor;

    public string Name => "User";
    public int Priority => 300;

    public UserSettingProvider(
        IRepository<Setting, Guid> repository,
        ICurrentUser? currentUser = null,
        ISettingEncryptor? settingEncryptor = null)
    {
        _repository = Check.NotNull(repository);
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

        // 加密值解不开（加密器缺席或密钥对不上）抛 SettingUnreadableException：返回 null 会让解析链
        // 悄悄落到低一级作用域的另一个值上，与「这一级没配」无从区分。
        if (setting.IsEncrypted)
            return setting.Value != null ? EncryptedSettingValue.Decrypt(_settingEncryptor, key, setting.Value) : null;

        return setting.Value;
    }
}
