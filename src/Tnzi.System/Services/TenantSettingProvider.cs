namespace Tnzi.System.Services;

/// <summary>
/// 租户级配置提供者（优先级中等）
/// </summary>
public class TenantSettingProvider : ISettingProvider
{
    private readonly IRepository<Setting, Guid> _repository;
    private readonly ICurrentTenant? _currentTenant;
    private readonly ISettingEncryptor? _settingEncryptor;
    private readonly ILogger<TenantSettingProvider> _logger;

    public string Name => "Tenant";
    public int Priority => 200;

    public TenantSettingProvider(
        IRepository<Setting, Guid> repository,
        ILogger<TenantSettingProvider> logger,
        ICurrentTenant? currentTenant = null,
        ISettingEncryptor? settingEncryptor = null)
    {
        _repository = Check.NotNull(repository);
        _logger = Check.NotNull(logger);
        _currentTenant = currentTenant;
        _settingEncryptor = settingEncryptor;
    }

    public async Task<string?> GetOrNullAsync(string key, CancellationToken cancellationToken = default)
    {
        var tenantId = _currentTenant?.Id;
        if (tenantId == null) return null;

        var scopeId = tenantId.Value.ToString();
        var setting = await _repository.AsQueryable()
            .AsNoTracking()
            .Where(s => s.Key == key && s.Scope == SettingScope.Tenant && s.ScopeId == scopeId)
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
