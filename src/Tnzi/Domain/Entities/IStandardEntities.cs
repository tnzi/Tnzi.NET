namespace Tnzi.Domain.Entities;

/// <summary>
/// 软删除接口
/// </summary>
public interface ISoftDelete
{
    bool IsDeleted { get; set; }
}

/// <summary>
/// 多租户接口
/// </summary>
public interface IMultiTenant
{
    Guid? TenantId { get; set; }
}

/// <summary>
/// 包含创建者接口 (用于数据权限)
/// </summary>
public interface IHasCreator
{
    Guid? CreatorId { get; set; }
}

/// <summary>
/// 包含修改者接口
/// </summary>
public interface IHasModifier
{
    Guid? LastModifierId { get; set; }
}

/// <summary>
/// 包含删除者接口
/// </summary>
public interface IHasDeleter
{
    Guid? DeleterId { get; set; }
    DateTime? DeletionTime { get; set; }
}

