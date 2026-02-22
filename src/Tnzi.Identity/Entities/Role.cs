namespace Tnzi.Identity.Entities;

/// <summary>
/// Tnzi 角色实体 (扩展自 ASP.NET Core Identity)
/// </summary>
[Table("Role")]
public class Role : IdentityRole<Guid>, IEntity<Guid>, IHasCreationTime
{
    public string? Description { get; set; }
    public DateTime CreationTime { get; set; }
    
    public object[] GetKeys() => new object[] { Id };
}
