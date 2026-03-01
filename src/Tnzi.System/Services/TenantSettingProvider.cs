namespace Tnzi.System.Services;

/// <summary>
/// 租户级配置提供者（优先级中等）
/// </summary>
public class TenantSettingProvider : ISettingProvider
{
    private readonly IRepository<Setting, Guid> _repository;
    private readonly ICurrentTenant? _currentTenant;

    public string Name => "Tenant";
    public int Priority => 200;

    public TenantSettingProvider(
        IRepository<Setting, Guid> repository,
        ICurrentTenant? currentTenant = null)
    {
        _repository = Check.NotNull(repository);
        _currentTenant = currentTenant;
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

        return setting?.Value;
    }
}
