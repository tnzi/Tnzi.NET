namespace Tnzi.Feature.Features;

/// <summary>
/// Action filter that checks if a feature is enabled before executing the action.
/// Used by <see cref="RequireFeatureAttribute"/> via TypeFilterAttribute.
/// </summary>
public class RequireFeatureFilter : IAsyncActionFilter
{
    private readonly string _featureName;
    private readonly IFeatureChecker _featureChecker;

    /// <summary>
    /// Initialize RequireFeatureFilter
    /// </summary>
    /// <param name="featureName">Feature name to check (injected from attribute arguments)</param>
    /// <param name="featureChecker">Feature checker service (injected from DI)</param>
    public RequireFeatureFilter(string featureName, IFeatureChecker featureChecker)
    {
        _featureName = Check.NotNullOrWhiteSpace(featureName);
        _featureChecker = Check.NotNull(featureChecker);
    }

    /// <inheritdoc />
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var isEnabled = await _featureChecker.IsEnabledAsync(_featureName);
        if (!isEnabled)
        {
            context.Result = new ObjectResult(
                ApiResult.Error($"Feature '{_featureName}' is not enabled", 403))
            {
                StatusCode = 403
            };
            return;
        }

        await next();
    }
}
