
namespace Tnzi.Identity.Mappings;

/// <summary>
/// Organization 映射配置
/// </summary>
public class OrganizationMappingConfig : IMappingConfig
{
    public void Configure(IMappingConfigContext context)
    {
        // Organization → OrganizationDto: 属性名完全一致，Mapster 自动映射，无需额外配置
        // Organization → OrganizationTreeItemDto: 同上
        // Organization → OrganizationTreeNodeDto: 同上，Children 由 BuildTree 在内存中组装

        // OrganizationTreeItemDto → OrganizationTreeNodeDto: 属性名完全一致
        // 用于 BuildTree 中的平面→树节点转换
        context.NewConfig<OrganizationTreeItemDto, OrganizationTreeNodeDto>()
            .Ignore(dest => dest.Children);
    }
}
