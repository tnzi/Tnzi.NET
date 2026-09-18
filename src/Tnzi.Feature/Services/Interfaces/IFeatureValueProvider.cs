namespace Tnzi.Feature.Services;

/// <summary>
/// Pluggable feature value provider.
/// Providers are evaluated in descending priority order.
/// </summary>
/// <remarks>
/// A provider is also the <b>write-side contract</b> for feature values: the admin API only
/// accepts a value for a provider that is registered here, because a row whose
/// <c>ProviderName</c> no provider ever reads is inert - the write returns 200 and the runtime
/// keeps answering the definition default, with nothing in the logs to tell the two apart.
/// The three default members below describe what a provider can accept so that
/// <c>IFeatureService</c> can refuse such writes up front instead of storing them.
/// </remarks>
public interface IFeatureValueProvider
{
    /// <summary>
    /// Provider name (e.g., "Global", "Tenant", "Edition"). Compared case-insensitively;
    /// the registered casing is what gets persisted.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Priority (higher values are evaluated first)
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Whether values for this provider are keyed (per tenant, per edition, ...).
    /// A keyless provider (<see cref="GlobalFeatureValueProvider"/>) stores exactly one value
    /// per feature and rejects a provider key - a keyed row under a keyless provider would
    /// never be matched by its <see cref="GetOrNullAsync"/>.
    /// </summary>
    bool RequiresKey => true;

    /// <summary>
    /// Whether this provider can resolve anything in the current deployment. A provider that
    /// is registered but structurally unable to run (the tenant provider with multi-tenancy
    /// disabled) reports <c>false</c> so the admin API refuses writes to it instead of
    /// accepting values that will never be evaluated.
    /// </summary>
    bool IsActive => true;

    /// <summary>
    /// Why <see cref="IsActive"/> is <c>false</c>, in user-facing English; <c>null</c> when active.
    /// </summary>
    string? InactiveReason => null;

    /// <summary>
    /// Whether a caller bound to tenant <paramref name="callerTenantId"/> may read or write the
    /// values stored under <paramref name="providerKey"/>. Host callers (no tenant) are never
    /// asked; they may address any key.
    /// </summary>
    /// <remarks>
    /// The admin API scopes feature values by the caller's <b>identity</b>, not by the key the
    /// client sends: <c>FeatureValue</c> does not implement <c>IMultiTenant</c> (the tenant lives
    /// inside <c>ProviderKey</c>), so the global tenant filter cannot do this. The default rule is
    /// fail-closed: a keyless provider is deployment-wide and always accessible; a keyed provider
    /// treats its key as a tenant id, so a tenant-bound caller only reaches its own tenant's
    /// values. A provider whose keys are something else (departments inside a tenant, ...)
    /// overrides this to say which keys the caller owns.
    /// </remarks>
    /// <param name="callerTenantId">The caller's tenant id, as persisted in <c>ProviderKey</c> (string form).</param>
    /// <param name="providerKey">The normalized provider key being addressed; <c>null</c> for a keyless provider.</param>
    bool IsKeyAccessibleTo(string callerTenantId, string? providerKey)
        => !RequiresKey || string.Equals(providerKey, callerTenantId, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Get the feature value for the current context, or null if not set
    /// </summary>
    /// <param name="featureName">Feature name</param>
    /// <returns>Feature value or null</returns>
    Task<string?> GetOrNullAsync(string featureName);
}
