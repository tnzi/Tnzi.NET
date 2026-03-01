namespace Tnzi.Feature.Mappings;

/// <summary>
/// Mapping configuration for FeatureValue entity
/// </summary>
public class FeatureValueMappingConfig : IMappingConfig
{
    /// <inheritdoc />
    public void Configure(IMappingConfigContext context)
    {
        // Map navigation property: FeatureDefinition.Name → FeatureName
        context.NewConfig<FeatureValue, FeatureValueDto>()
            .Map(dest => dest.FeatureName, src => src.FeatureDefinition != null ? src.FeatureDefinition.Name : null);
    }
}
