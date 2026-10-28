namespace Tnzi.Authorization.Services;

/// <summary>
/// 功能目录实体到管理端读 DTO 的映射。只投影管理面需要的字段，审计与导航字段不出 API。
/// </summary>
internal static class PermissionCatalogueMapping
{
    public static ModuleFunctionDto ToFunctionDto(ModuleFunction entity) => new()
    {
        Id = entity.Id,
        Name = entity.Name,
        Code = entity.Code,
        Description = entity.Description,
        ModuleId = entity.ModuleId,
        IsEnabled = entity.IsEnabled,
        Order = entity.Order,
        IsSystemManaged = entity.IsSystemManaged,
        Category = entity.Category,
        IsRetired = entity.IsRetired,
    };

    public static PermissionCatalogueModuleDto ToCatalogueModule(FunctionModule module, IEnumerable<ModuleFunction> functions, bool isBuiltIn) => new()
    {
        Id = module.Id,
        Name = module.Name,
        Code = module.Code,
        Description = module.Description,
        Order = module.Order,
        IsEnabled = module.IsEnabled,
        ParentId = module.ParentId,
        IsSystemManaged = module.IsSystemManaged,
        IsRetired = module.IsRetired,
        IsBuiltIn = isBuiltIn,
        Functions = functions.OrderBy(f => f.Order).Select(ToFunctionDto).ToList(),
    };
}
