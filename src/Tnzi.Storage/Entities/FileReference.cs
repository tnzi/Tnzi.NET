namespace Tnzi.Storage.Entities;

/// <summary>
/// 文件引用记录 - 记录文件被哪个表哪个字段引用
/// </summary>
public class FileReference : EntityBase<Guid>, IHasCreationTime
{
    /// <summary>
    /// 文件ID
    /// </summary>
    public Guid FileId { get; set; }

    /// <summary>
    /// 引用的实体类型（如 "User"）
    /// </summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>
    /// 引用的实体ID
    /// </summary>
    public Guid EntityId { get; set; }

    /// <summary>
    /// 引用的字段名（如 "Avatar"）
    /// </summary>
    public string FieldName { get; set; } = string.Empty;

    /// <summary>
    /// 是否临时引用（未提交表单前的引用）
    /// </summary>
    public bool IsTemporary { get; set; } = true;

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreationTime { get; set; }
}
