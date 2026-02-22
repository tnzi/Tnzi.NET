namespace Tnzi.Identity.Entities;

/// <summary>
/// Tnzi 用户角色关系实体
/// </summary>
[Table("UserRole")]
public class UserRole : IdentityUserRole<Guid>, IEntity
{
    // 联合主键
    public object[] GetKeys() => new object[] { UserId, RoleId };
}
