
namespace Tnzi.EFCore.Tests.TestEntities;

/// <summary>
/// 测试用户实体
/// </summary>
public class TestUser : EntityBase<Guid>, IHasCreationTime, IHasCreator, IHasModificationTime, IHasModifier, ISoftDelete, IHasDeleter, IMultiTenant
{
    /// <summary>
    /// 用户名
    /// </summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>
    /// 邮箱
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// 头像文件ID (单个文件)
    /// </summary>
    [FileField]
    public Guid? AvatarFileId { get; set; }

    /// <summary>
    /// 附件文件ID列表 (多个文件,JSON数组)
    /// </summary>
    [FileField]
    public string? AttachmentFileIds { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreationTime { get; set; }

    /// <summary>
    /// 创建者ID
    /// </summary>
    public Guid? CreatorId { get; set; }

    /// <summary>
    /// 最后修改时间
    /// </summary>
    public DateTime? LastModificationTime { get; set; }

    /// <summary>
    /// 最后修改者ID
    /// </summary>
    public Guid? LastModifierId { get; set; }

    /// <summary>
    /// 是否已删除
    /// </summary>
    public bool IsDeleted { get; set; }

    /// <summary>
    /// 删除者ID
    /// </summary>
    public Guid? DeleterId { get; set; }

    /// <summary>
    /// 删除时间
    /// </summary>
    public DateTime? DeletionTime { get; set; }

    /// <summary>
    /// 租户ID
    /// </summary>
    public Guid? TenantId { get; set; }
}