namespace Tnzi.Identity.Entities;

/// <summary>
/// Tnzi 用户声明实体
/// </summary>
[Table("UserClaim")]
public class UserClaim : IdentityUserClaim<Guid>, IEntity
{
    public object[] GetKeys() => new object[] { Id };
}
