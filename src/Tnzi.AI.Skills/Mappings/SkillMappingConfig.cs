using Tnzi.Mapping;

namespace Tnzi.AI.Skills.Mappings;

/// <summary>
/// Mapster mapping configuration for skill types.
/// </summary>
public class SkillMappingConfig : IMappingConfig
{
    public void Configure(IMappingConfigContext context)
    {
        // SkillDefinition → SkillSummaryDto
        // Id, CreationTime, LastModificationTime are not in SkillDefinition — ignore them
        context.NewConfig<SkillDefinition, SkillSummaryDto>()
            .Ignore(dest => dest.Id)
            .Ignore(dest => dest.CreationTime)
            .Ignore(dest => dest.LastModificationTime);

        // SkillDefinition → SkillDetailDto
        // Inherits SkillSummaryDto ignores + OwnerUserId is not in SkillDefinition
        context.NewConfig<SkillDefinition, SkillDetailDto>()
            .Ignore(dest => dest.Id)
            .Ignore(dest => dest.CreationTime)
            .Ignore(dest => dest.LastModificationTime)
            .Ignore(dest => dest.OwnerUserId);
    }
}
