namespace Tnzi.System.Services;

/// <summary>
/// 全局配置提供者（优先级最低）
/// </summary>
public class GlobalSettingProvider : ISettingProvider
{
    private readonly IRepository<Setting, Guid> _repository;
    private readonly ISettingEncryptor? _settingEncryptor;
    private readonly ILogger<GlobalSettingProvider> _logger;

    public string Name => "Global";
    public int Priority => 100;

    public GlobalSettingProvider(
        IRepository<Setting, Guid> repository,
        ILogger<GlobalSettingProvider> logger,
        ISettingEncryptor? settingEncryptor = null)
    {
        _repository = Check.NotNull(repository);
        _logger = Check.NotNull(logger);
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
