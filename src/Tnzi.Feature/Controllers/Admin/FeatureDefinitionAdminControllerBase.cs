namespace Tnzi.Feature.Controllers.Admin;

/// <summary>
/// Feature definition admin controller base class.
/// Provides CRUD endpoints for feature definitions.
/// </summary>
[Route("admin/feature-definitions")]
[ApiExplorerSettings(GroupName = "feature-admin")]
public abstract class FeatureDefinitionAdminControllerBase : ApiAdminControllerBase
{
    protected readonly IFeatureService FeatureService;

    /// <summary>
    /// Initialize FeatureDefinitionAdminControllerBase
    /// </summary>
    protected FeatureDefinitionAdminControllerBase(IFeatureService featureService)
    {
        FeatureService = Check.NotNull(featureService);
    }

    /// <summary>
    /// Get all feature definitions
    /// </summary>
    [HttpGet]
    public virtual async Task<ApiResult<IEnumerable<FeatureDefinitionDto>>> GetAll()
    {
        var result = await FeatureService.GetDefinitionsAsync();
        return result.ToApiResult();
    }

    /// <summary>
    /// Get a feature definition by ID
    /// </summary>
    /// <param name="id">Feature definition ID</param>
    [HttpGet("{id:guid}")]
    public virtual async Task<ApiResult<FeatureDefinitionDto>> GetById(Guid id)
    {
        var result = await FeatureService.GetDefinitionByIdAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// Create a new feature definition
    /// </summary>
    /// <param name="request">Create request</param>
    [HttpPost]
    public virtual async Task<ApiResult<FeatureDefinitionDto>> Create([FromBody] CreateFeatureDefinitionRequest request)
    {
        var result = await FeatureService.CreateDefinitionAsync(request);
        return result.ToApiResult();
    }

    /// <summary>
    /// Update a feature definition
    /// </summary>
    /// <param name="id">Feature definition ID</param>
    /// <param name="request">Update request</param>
    [HttpPut("{id:guid}")]
    public virtual async Task<ApiResult<FeatureDefinitionDto>> Update(Guid id, [FromBody] UpdateFeatureDefinitionRequest request)
    {
        var result = await FeatureService.UpdateDefinitionAsync(id, request);
        return result.ToApiResult();
    }

    /// <summary>
    /// Delete a feature definition
    /// </summary>
    /// <param name="id">Feature definition ID</param>
    [HttpDelete("{id:guid}")]
    public virtual async Task<ApiResult> Delete(Guid id)
    {
        var result = await FeatureService.DeleteDefinitionAsync(id);
        return result.ToApiResult();
    }
}
