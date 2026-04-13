using Tnzi.Mapping;

namespace Tnzi.AI.Mappings;

/// <summary>
/// ToolPermissionRuleEntity mapping configuration.
/// Handles int ↔ enum casting for Behavior (PermissionBehavior) and Scope (ToolPermissionScope).
/// </summary>
public class PermissionMappingConfig : IMappingConfig
{
    public void Configure(IMappingConfigContext context)
    {
        // ToolPermissionRuleEntity → PersistedPermissionRuleDto
        context.NewConfig<ToolPermissionRuleEntity, PersistedPermissionRuleDto>()
            .Map(dest => dest.Behavior, src => (PermissionBehavior)src.Behavior)
            .Map(dest => dest.Scope, src => (ToolPermissionScope)src.Scope);

        // CreatePersistedPermissionRuleDto → ToolPermissionRuleEntity
        context.NewConfig<CreatePersistedPermissionRuleDto, ToolPermissionRuleEntity>()
            .Map(dest => dest.Behavior, src => (int)src.Behavior)
            .Map(dest => dest.Scope, src => (int)src.Scope);

        // ToolPermissionRuleEntity → ToolPermissionRule
        context.NewConfig<ToolPermissionRuleEntity, ToolPermissionRule>()
            .Map(dest => dest.Behavior, src => (PermissionBehavior)src.Behavior)
            .Map(dest => dest.Scope, src => (ToolPermissionScope)src.Scope)
            .AfterMapping((src, dest) =>
            {
                // ToolPattern defaults to "*" when null
                dest.ToolPattern ??= "*";
            });
    }
}
