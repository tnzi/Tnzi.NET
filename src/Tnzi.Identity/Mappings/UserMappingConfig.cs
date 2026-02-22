
namespace Tnzi.Identity.Mappings;

/// <summary>
/// User 映射配置
/// </summary>
public class UserMappingConfig : IMappingConfig
{
    public void Configure(IMappingConfigContext context)
    {
        // User → UserListItemDto（列表查询，不含 UserDetail 字段）
        context.NewConfig<User, UserListItemDto>()
            .Map(dest => dest.IsEmailConfirmed, src => src.EmailConfirmed)
            .Map(dest => dest.IsPhoneNumberConfirmed, src => src.PhoneNumberConfirmed)
            .Map(dest => dest.IsLockedOut, src => src.LockoutEnd.HasValue && src.LockoutEnd.Value > DateTimeOffset.UtcNow)
            .Map(dest => dest.OrganizationName, src => src.Organization != null ? src.Organization.Name : null)
            .Ignore(dest => dest.Roles); // Roles 在服务层批量加载

        // User → UserDto（单用户详情，同样需要基础字段映射）
        // UserDetail 字段（Nickname, Avatar 等）在服务层手动填充
        context.NewConfig<User, UserDto>()
            .Map(dest => dest.IsEmailConfirmed, src => src.EmailConfirmed)
            .Map(dest => dest.IsPhoneNumberConfirmed, src => src.PhoneNumberConfirmed)
            .Map(dest => dest.IsLockedOut, src => src.LockoutEnd.HasValue && src.LockoutEnd.Value > DateTimeOffset.UtcNow)
            .Map(dest => dest.OrganizationName, src => src.Organization != null ? src.Organization.Name : null)
            .Ignore(dest => dest.Roles);
    }
}
