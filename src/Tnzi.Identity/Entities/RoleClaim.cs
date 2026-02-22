namespace Tnzi.Identity.Entities;

/// <summary>
/// Tnzi 角色声明实体
/// </summary>
[Table("RoleClaim")]
public class RoleClaim : IdentityRoleClaim<Guid>, IEntity
{
    public object[] GetKeys() => new object[] { Id };
}
