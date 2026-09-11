namespace Tnzi.Feature.Services;

/// <summary>
/// Deployment-wide feature value provider.
/// Reads the single keyless value stored for a feature (ProviderName "Global", no ProviderKey).
/// Priority: 100 - evaluated after every scoped provider (Tenant = 200) and before the
/// definition default, so a tenant override still wins and an unset tenant falls through here.
/// </summary>
/// <remarks>
/// This is the provider that makes feature values work at all in a single-tenant deployment:
/// before it existed the only built-in provider was tenant-scoped, and with multi-tenancy
/// disabled every value written through the admin API was accepted and then never read.
/// Lookups go through <see cref="FeatureValueCache"/> when one is available, so a hot
/// <c>[RequireFeature]</c> endpoint does not query the value table on every request.
/// </remarks>
public class GlobalFeatureValueProvider : IFeatureValueProvider
{
    /// <summary>Canonical provider name, as persisted in <c>FeatureValue.ProviderName</c>.</summary>
    public const string ProviderName = "Global";

    private readonly IRepository<FeatureValue, Guid> _repository;
    private readonly FeatureValueCache? _cache;

    /// <inheritdoc />
    public string Name => ProviderName;

    /// <inheritdoc />
    public int Priority => 100;

    /// <inheritdoc />
    public bool RequiresKey => false;

    /// <summary>
    /// Initialize GlobalFeatureValueProvider
    /// </summary>
    /// <param name="repository">Feature value repository.</param>
    /// <param name="cache">
    /// Optional per-scope value cache. Absent (unit tests, custom hosts that do not register it)
    /// every lookup queries the database.
    /// </param>
    public GlobalFeatureValueProvider(IRepository<FeatureValue, Guid> repository, FeatureValueCache? cache = null)
    {
        _repository = Check.NotNull(repository);
        _cache = cache;
    }

    /// <inheritdoc />
    public Task<string?> GetOrNullAsync(string featureName)
    {
        Check.NotNullOrWhiteSpace(featureName);

        return _cache == null
            ? QueryAsync(featureName)
            : _cache.GetOrLoadAsync(ProviderName, null, featureName, () => QueryAsync(featureName));
    }

    private async Task<string?> QueryAsync(string featureName)
    {
        return await _repository
            .Where(fv => fv.ProviderName == ProviderName
                         && fv.ProviderKey == null
                         && fv.FeatureDefinition != null
                         && fv.FeatureDefinition.Name == featureName)
            .Select(fv => fv.Value)
            .FirstOrDefaultAsync();
    }
}
