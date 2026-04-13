using Tnzi.Mapping;

namespace Tnzi.AI.Mappings;

/// <summary>
/// SubAgentType entity/DTO mapping configuration.
/// Handles JSON array serialization (string? ↔ List&lt;string&gt;?) and enum casting (int? ↔ ToolApprovalMode?).
/// </summary>
public class SubAgentTypeMappingConfig : IMappingConfig
{
    public void Configure(IMappingConfigContext context)
    {
        // SubAgentType → SubAgentTypeDto
        context.NewConfig<SubAgentType, SubAgentTypeDto>()
            .Map(dest => dest.ToolGroups, src => src.ToolGroupsJson.FromJsonString<List<string>>())
            .Map(dest => dest.ExcludedToolGroups, src => src.ExcludedToolGroupsJson.FromJsonString<List<string>>())
            .Map(dest => dest.CapabilityTags, src => src.CapabilityTagsJson.FromJsonString<List<string>>())
            .Map(dest => dest.DefaultApprovalMode,
                src => src.DefaultApprovalMode.HasValue ? (ToolApprovalMode)src.DefaultApprovalMode.Value : (ToolApprovalMode?)null);

        // SubAgentTypeInputDto → SubAgentType
        context.NewConfig<SubAgentTypeInputDto, SubAgentType>()
            .Map(dest => dest.ToolGroupsJson, src => src.ToolGroups != null ? src.ToolGroups.ToJsonString() : null)
            .Map(dest => dest.ExcludedToolGroupsJson, src => src.ExcludedToolGroups != null ? src.ExcludedToolGroups.ToJsonString() : null)
            .Map(dest => dest.CapabilityTagsJson, src => src.CapabilityTags != null ? src.CapabilityTags.ToJsonString() : null)
            .Map(dest => dest.DefaultApprovalMode,
                src => src.DefaultApprovalMode.HasValue ? (int?)((int)src.DefaultApprovalMode.Value) : null);
    }
}
