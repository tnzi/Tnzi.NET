namespace Tnzi.Feature.Metadata;

/// <summary>
/// Feature module error code constants.
/// </summary>
public static class ErrorCodes
{
    /// <summary>
    /// Feature definition not found.
    /// </summary>
    public const string FeatureDefinitionNotFound = "FEATURE_DEFINITION_NOT_FOUND";

    /// <summary>
    /// Feature definition already exists.
    /// </summary>
    public const string FeatureDefinitionAlreadyExists = "FEATURE_DEFINITION_ALREADY_EXISTS";

    /// <summary>
    /// Feature value not found.
    /// </summary>
    public const string FeatureValueNotFound = "FEATURE_VALUE_NOT_FOUND";

    /// <summary>
    /// Feature value already exists.
    /// </summary>
    public const string FeatureValueAlreadyExists = "FEATURE_VALUE_ALREADY_EXISTS";

    /// <summary>
    /// Feature is disabled.
    /// </summary>
    public const string FeatureDisabled = "FEATURE_DISABLED";

    /// <summary>
    /// Invalid feature value type.
    /// </summary>
    public const string InvalidFeatureValueType = "FEATURE_INVALID_VALUE_TYPE";

    /// <summary>
    /// The provider named in a value request is not registered as an <c>IFeatureValueProvider</c>,
    /// so nothing would ever read the value.
    /// </summary>
    public const string FeatureValueProviderUnknown = "FEATURE_VALUE_PROVIDER_UNKNOWN";

    /// <summary>
    /// The provider is registered but cannot evaluate anything in this deployment
    /// (e.g. the tenant provider with multi-tenancy disabled).
    /// </summary>
    public const string FeatureValueProviderInactive = "FEATURE_VALUE_PROVIDER_INACTIVE";

    /// <summary>
    /// The provider is keyed (per tenant, per edition, ...) and no provider key was given.
    /// </summary>
    public const string FeatureValueProviderKeyRequired = "FEATURE_VALUE_PROVIDER_KEY_REQUIRED";

    /// <summary>
    /// The provider is keyless (deployment-wide) and a provider key was given.
    /// </summary>
    public const string FeatureValueProviderKeyNotAllowed = "FEATURE_VALUE_PROVIDER_KEY_NOT_ALLOWED";

    /// <summary>
    /// The feature definition comes from code (<c>IFeatureDefinitionProvider</c>) and has no
    /// database row, so no value can be attached to it yet.
    /// </summary>
    public const string FeatureDefinitionNotOverridable = "FEATURE_DEFINITION_NOT_OVERRIDABLE";

    /// <summary>
    /// The caller is bound to a tenant and named a provider key it does not own
    /// (typically another tenant's id). Refused, never silently redirected to the caller's tenant.
    /// </summary>
    public const string FeatureValueScopeForbidden = "FEATURE_VALUE_SCOPE_FORBIDDEN";
}
