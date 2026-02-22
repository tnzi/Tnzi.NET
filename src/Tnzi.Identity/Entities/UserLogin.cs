namespace Tnzi.Identity.Entities;

/// <summary>
/// Tnzi 用户登录实体 (第三方登录绑定)
/// </summary>
[Table("UserLogin")]
public class UserLogin : IdentityUserLogin<Guid>, IEntity
{
    // 联合主键
    public object[] GetKeys() => new object[] { LoginProvider, ProviderKey };
}
