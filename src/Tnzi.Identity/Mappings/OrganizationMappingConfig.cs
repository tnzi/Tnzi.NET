
namespace Tnzi.Identity.Mappings;

/// <summary>
/// Organization 映射配置
/// </summary>
public class OrganizationMappingConfig : IMappingConfig
{
    public void Configure(IMappingConfigContext context)
    {
        // Organization → OrganizationDto
        // 属性名一致的会自动映射
        // Children 需要递归映射，在服务层处理
        context.NewConfig<Organization, OrganizationDto>()
            .Ignore(dest => dest.Children); // Children 在服务层手动构建树结构
    }
}