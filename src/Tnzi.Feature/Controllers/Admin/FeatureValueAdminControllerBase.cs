namespace Tnzi.Feature.Controllers.Admin;

/// <summary>
/// Feature value admin controller base class.
/// Provides endpoints for managing feature values per provider.
/// </summary>
[Route("admin/feature-values")]
[ApiExplorerSettings(GroupName = "feature-admin")]
public abstract class FeatureValueAdminControllerBase : ApiAdminControllerBase
{
    protected readonly IFeatureService FeatureService;

    /// <summary>
    /// Initialize FeatureValueAdminControllerBase
    /// </summary>
    protected FeatureValueAdminControllerBase(IFeatureService featureService)
    {
        FeatureService = Check.NotNull(featureService);
    }

    /// <summary>
    /// Get feature values for a specific provider
    /// </summary>
    /// <param name="providerName">Provider name (e.g., "Tenant", "Edition")</param>
    /// <param name="providerKey">Provider key (optional)</param>
    [HttpGet]
    public virtual async Task<ApiResult<IEnumerable<FeatureValueDto>>> GetValues(
        [FromQuery] string providerName,
        [FromQuery] string? providerKey = null)
    {
        var result = await FeatureService.GetValuesAsync(providerName, providerKey);
        return result.ToApiResult();
    }

    /// <summary>
    /// Set a feature value for a provider
    /// </summary>
    /// <param name="request">Set value request</param>
    [HttpPost]
    public virtual async Task<ApiResult<FeatureValueDto>> SetValue([FromBody] SetFeatureValueRequest request)
    {
        var result = await FeatureService.SetValueAsync(request);
        return result.ToApiResult();
    }

    /// <summary>
    /// Delete a feature value
    /// </summary>
    /// <param name="id">Feature value ID</param>
    [HttpDelete("{id:guid}")]
    public virtual async Task<ApiResult> Delete(Guid id)
    {
        var result = await FeatureService.DeleteValueAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// Batch set multiple feature values for a provider
    /// </summary>
    /// <param name="request">Batch set request</param>
    [HttpPost("batch")]
    public virtual async Task<ApiResult<BatchSetFeatureValuesResultDto>> BatchSetValues([FromBody] BatchSetFeatureValuesRequest request)
    {
        var result = await FeatureService.BatchSetValuesAsync(request);
        return result.ToApiResult();
    }

    /// <summary>
    /// Get all feature values for a provider (including definitions and effective values)
    /// </summary>
    /// <param name="providerName">Provider name</param>
    /// <param name="providerKey">Provider key (optional)</param>
    [HttpGet("all")]
    public virtual async Task<ApiResult<IEnumerable<FeatureValueWithDefinitionDto>>> GetAllValues(
        [FromQuery] string providerName,
        [FromQuery] string? providerKey = null)
    {
        var result = await FeatureService.GetAllValuesAsync(providerName, providerKey);
        return result.ToApiResult();
    }
}
