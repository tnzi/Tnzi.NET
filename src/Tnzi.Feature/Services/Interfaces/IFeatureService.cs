namespace Tnzi.Feature.Services;

/// <summary>
/// Admin feature management service interface.
/// Provides CRUD operations for feature definitions and values.
/// </summary>
public interface IFeatureService
{
    // ==================== Feature Definitions ====================

    /// <summary>
    /// Get all feature definitions
    /// </summary>
    Task<Result<IEnumerable<FeatureDefinitionDto>>> GetDefinitionsAsync();

    /// <summary>
    /// Get a feature definition by ID
    /// </summary>
    /// <param name="id">Feature definition ID</param>
    Task<Result<FeatureDefinitionDto>> GetDefinitionByIdAsync(Guid id);

    /// <summary>
    /// Create a new feature definition
    /// </summary>
    /// <param name="input">Create request</param>
    Task<Result<FeatureDefinitionDto>> CreateDefinitionAsync(CreateFeatureDefinitionRequest input);

    /// <summary>
    /// Update a feature definition
    /// </summary>
    /// <param name="id">Feature definition ID</param>
    /// <param name="input">Update request</param>
    Task<Result<FeatureDefinitionDto>> UpdateDefinitionAsync(Guid id, UpdateFeatureDefinitionRequest input);

    /// <summary>
    /// Delete a feature definition
    /// </summary>
    /// <param name="id">Feature definition ID</param>
    Task<Result> DeleteDefinitionAsync(Guid id);

    // ==================== Feature Values ====================

    /// <summary>
    /// Get feature values for a specific provider
    /// </summary>
    /// <param name="providerName">Provider name (e.g., "Tenant", "Edition")</param>
    /// <param name="providerKey">Provider key (e.g., tenantId)</param>
    Task<Result<IEnumerable<FeatureValueDto>>> GetValuesAsync(string providerName, string? providerKey);

    /// <summary>
    /// Set a feature value for a provider
    /// </summary>
    /// <param name="input">Set value request</param>
    Task<Result<FeatureValueDto>> SetValueAsync(SetFeatureValueRequest input);

    /// <summary>
    /// Delete a feature value
    /// </summary>
    /// <param name="id">Feature value ID</param>
    Task<Result> DeleteValueAsync(Guid id);

    /// <summary>
    /// Batch set multiple feature values for a provider
    /// </summary>
    /// <param name="input">Batch set request</param>
    Task<Result<BatchSetFeatureValuesResultDto>> BatchSetValuesAsync(BatchSetFeatureValuesRequest input)
    {
        return Task.FromResult(Result.Failure<BatchSetFeatureValuesResultDto>("Batch set values not implemented", 501));
    }

    /// <summary>
    /// Get all feature values for a provider, including definition info and effective values
    /// </summary>
    /// <param name="providerName">Provider name</param>
    /// <param name="providerKey">Provider key (optional)</param>
    Task<Result<IEnumerable<FeatureValueWithDefinitionDto>>> GetAllValuesAsync(string providerName, string? providerKey)
    {
        return Task.FromResult(Result.Failure<IEnumerable<FeatureValueWithDefinitionDto>>("Get all values not implemented", 501));
    }
}
