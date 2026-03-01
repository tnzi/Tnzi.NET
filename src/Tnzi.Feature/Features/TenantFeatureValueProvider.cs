namespace Tnzi.Feature.Features;

/// <summary>
/// Tenant-level feature value provider.
/// Reads feature values assigned to the current tenant.
/// Priority: 200 (evaluated before EditionFeatureValueProvider)
/// </summary>
public class TenantFeatureValueProvider : IFeatureValueProvider
{
    private readonly IRepository<FeatureValue, Guid> _repository;
    private readonly ICurrentTenant? _currentTenant;

    /// <inheritdoc />
    public string Name => "Tenant";

    /// <inheritdoc />
    public int Priority => 200;

    /// <summary>
    /// Initialize TenantFeatureValueProvider
    /// </summary>
    public TenantFeatureValueProvider(
        IRepository<FeatureValue, Guid> repository,
        ICurrentTenant? currentTenant = null)
    {
        _repository = Check.NotNull(repository);
        _currentTenant = currentTenant;
    }

    /// <inheritdoc />
    public async Task<string?> GetOrNullAsync(string featureName)
    {
        if (_currentTenant == null || !_currentTenant.IsAvailable)
            return null;

        var tenantId = _currentTenant.Id?.ToString();
        if (string.IsNullOrEmpty(tenantId))
            return null;

        var featureValue = await _repository
            .Where(fv => fv.ProviderName == Name
                         && fv.ProviderKey == tenantId
                         && fv.FeatureDefinition != null
                         && fv.FeatureDefinition.Name == featureName)
            .Select(fv => fv.Value)
            .FirstOrDefaultAsync();

        return featureValue;
    }
}
