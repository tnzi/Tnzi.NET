namespace Tnzi.Identity.Entities;

/// <summary>
/// Tnzi 用户令牌实体
/// </summary>
[Table("UserToken")]
public class UserToken : IdentityUserToken<Guid>, IEntity
{
    // 联合主键
    public object[] GetKeys() => new object[] { UserId, LoginProvider, Name };
}
