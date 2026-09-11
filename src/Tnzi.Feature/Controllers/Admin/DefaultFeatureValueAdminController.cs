namespace Tnzi.Feature.Controllers.Admin;

/// <summary>
/// Feature value admin controller.
/// Provides endpoints for managing feature values per provider.
/// </summary>
[DefaultController]
[Route("admin/feature-values")]
[ApiAuthorize(PermissionName = "feature.view")]
public class DefaultFeatureValueAdminController : ApiAdminControllerBase
{
    protected readonly IFeatureService FeatureService;

    /// <summary>
    /// Initialize FeatureValueAdminController
    /// </summary>
    public DefaultFeatureValueAdminController(IFeatureService featureService)
    {
        FeatureService = Check.NotNull(featureService);
    }

    /// <summary>
    /// List the registered value providers (scopes) - the only names <c>POST</c> accepts.
    /// The admin UI builds its scope picker from this so an operator cannot write to a scope
    /// nothing reads. Inactive providers are listed with the reason so existing rows stay
    /// visible for cleanup.
    /// </summary>
    [HttpGet("providers")]
    public virtual async Task<ApiResult<IEnumerable<FeatureValueProviderDto>>> GetProviders()
    {
        var result = await FeatureService.GetValueProvidersAsync();
        return result.ToApiResult();
    }

    /// <summary>
    /// Get feature values for a specific provider
    /// </summary>
    /// <param name="providerName">Provider name (e.g., "Global", "Tenant")</param>
    /// <param name="providerKey">Provider key (required for keyed providers such as Tenant)</param>
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
    [ApiAuthorize(PermissionName = "feature.update")]
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
    [ApiAuthorize(PermissionName = "feature.delete")]
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
    [ApiAuthorize(PermissionName = "feature.update")]
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
