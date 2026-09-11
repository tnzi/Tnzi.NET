namespace Tnzi.Feature.Services;

/// <summary>
/// Tenant-level feature value provider.
/// Reads feature values assigned to the current tenant.
/// Priority: 200 (evaluated before <see cref="GlobalFeatureValueProvider"/> at 100)
/// </summary>
/// <remarks>
/// Lookups go through <see cref="FeatureValueCache"/> (keyed by tenant id) when one is
/// available, so a hot <c>[RequireFeature]</c> endpoint does not query the value table on
/// every request. The "no tenant on this request" early exits never touch cache or database.
/// </remarks>
public class TenantFeatureValueProvider : IFeatureValueProvider
{
    /// <summary>Canonical provider name, as persisted in <c>FeatureValue.ProviderName</c>.</summary>
    public const string ProviderName = "Tenant";

    private readonly IRepository<FeatureValue, Guid> _repository;
    private readonly ICurrentTenant? _currentTenant;
    private readonly bool _multiTenancyEnabled;
    private readonly FeatureValueCache? _cache;

    /// <inheritdoc />
    public string Name => ProviderName;

    /// <inheritdoc />
    public int Priority => 200;

    /// <summary>
    /// Active only when multi-tenancy is switched on. With it off no request ever carries a
    /// tenant, so <see cref="GetOrNullAsync"/> returns null for every feature and a value
    /// written under this provider is dead on arrival - which the admin API must refuse rather
    /// than confirm.
    /// </summary>
    public bool IsActive => _multiTenancyEnabled;

    /// <inheritdoc />
    public string? InactiveReason => _multiTenancyEnabled
        ? null
        : "multi-tenancy is disabled (MultiTenancy:Enabled=false), so no request carries a tenant and tenant-scoped values are never evaluated";

    /// <summary>
    /// Initialize TenantFeatureValueProvider
    /// </summary>
    /// <param name="repository">Feature value repository.</param>
    /// <param name="currentTenant">Current tenant accessor (absent when multi-tenancy is not loaded).</param>
    /// <param name="multiTenancyOptions">Multi-tenancy options; absent reads as disabled.</param>
    /// <param name="cache">
    /// Optional per-scope value cache. Absent (unit tests, custom hosts that do not register it)
    /// every lookup queries the database.
    /// </param>
    public TenantFeatureValueProvider(
        IRepository<FeatureValue, Guid> repository,
        ICurrentTenant? currentTenant = null,
        IOptions<MultiTenancyOptions>? multiTenancyOptions = null,
        FeatureValueCache? cache = null)
    {
        _repository = Check.NotNull(repository);
        _currentTenant = currentTenant;
        _multiTenancyEnabled = multiTenancyOptions?.Value.Enabled ?? false;
        _cache = cache;
    }

    /// <inheritdoc />
    public Task<string?> GetOrNullAsync(string featureName)
    {
        if (_currentTenant == null || !_currentTenant.IsAvailable)
            return Task.FromResult<string?>(null);

        var tenantId = _currentTenant.Id?.ToString();
        if (string.IsNullOrEmpty(tenantId))
            return Task.FromResult<string?>(null);

        return _cache == null
            ? QueryAsync(tenantId, featureName)
            : _cache.GetOrLoadAsync(ProviderName, tenantId, featureName, () => QueryAsync(tenantId, featureName));
    }

    private async Task<string?> QueryAsync(string tenantId, string featureName)
    {
        return await _repository
            .Where(fv => fv.ProviderName == ProviderName
                         && fv.ProviderKey == tenantId
                         && fv.FeatureDefinition != null
                         && fv.FeatureDefinition.Name == featureName)
            .Select(fv => fv.Value)
            .FirstOrDefaultAsync();
    }
}
