namespace Tnzi.Feature.Features;

/// <summary>
/// Attribute for declarative feature gating on controllers or actions.
/// When applied, the action will return 403 if the specified feature is not enabled.
/// </summary>
/// <example>
/// [RequireFeature("Premium.Export")]
/// public async Task&lt;ApiResult&lt;byte[]&gt;&gt; ExportData() { ... }
/// </example>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public class RequireFeatureAttribute : TypeFilterAttribute
{
    /// <summary>
    /// Feature name to check
    /// </summary>
    public string FeatureName { get; }

    /// <summary>
    /// Initialize RequireFeatureAttribute
    /// </summary>
    /// <param name="featureName">Feature name that must be enabled</param>
    public RequireFeatureAttribute(string featureName)
        : base(typeof(RequireFeatureFilter))
    {
        FeatureName = Check.NotNullOrWhiteSpace(featureName);
        Arguments = [featureName];
    }
}
