namespace Tnzi.System.Services;

/// <summary>
/// 全局配置提供者（优先级最低）
/// </summary>
public class GlobalSettingProvider : ISettingProvider
{
    private readonly IRepository<Setting, Guid> _repository;
    private readonly ISettingEncryptor? _settingEncryptor;

    public string Name => "Global";
    public int Priority => 100;

    public GlobalSettingProvider(
        IRepository<Setting, Guid> repository,
        ISettingEncryptor? settingEncryptor = null)
    {
        _repository = Check.NotNull(repository);
        _settingEncryptor = settingEncryptor;
    }

    public async Task<string?> GetOrNullAsync(string key, CancellationToken cancellationToken = default)
    {
        var setting = await _repository.AsQueryable()
            .AsNoTracking()
            .Where(s => s.Key == key && s.Scope == SettingScope.Global)
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
