namespace Tnzi.Storage;

/// <summary>
/// 文件引用变更类型
/// </summary>
public enum FileReferenceChangeType
{
    /// <summary>
    /// 创建引用
    /// </summary>
    Create,
    
    /// <summary>
    /// 删除引用
    /// </summary>
    Delete
}

/// <summary>
/// 文件引用变更信息
/// </summary>
public class FileReferenceChange
{
    /// <summary>
    /// 变更类型
    /// </summary>
    public FileReferenceChangeType ChangeType { get; set; }
    
    /// <summary>
    /// 实体类型名称
    /// </summary>
    public string EntityType { get; set; } = string.Empty;
    
    /// <summary>
    /// 实体ID（字符串形式，支持 Guid、long、int、string 等多种 ID 类型）
    /// </summary>
    public string EntityId { get; set; } = string.Empty;
    
    /// <summary>
    /// 字段名
    /// </summary>
    public string FieldName { get; set; } = string.Empty;
    
    /// <summary>
    /// 文件ID
    /// </summary>
    public Guid FileId { get; set; }

    /// <summary>
    /// 来源字段是否声明为公开（<c>[FileField(Public = true)]</c>）。
    /// 为 true 时处理器在写入引用的同一个事务里把该文件标记为公开可读。
    /// 仅对 <see cref="FileReferenceChangeType.Create"/> 有意义：移除引用不收回公开标记。
    /// </summary>
    public bool IsPublicFile { get; set; }
}

/// <summary>
/// 文件引用处理器接口
/// 由 FileStorage 模块实现，在 EFCore 中调用
/// </summary>
public interface IFileReferenceProcessor
{
    /// <summary>
    /// 处理文件引用变更（在数据库事务中调用）
    /// </summary>
    /// <param name="changes">变更列表</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task ProcessChangesAsync(IReadOnlyList<FileReferenceChange> changes, CancellationToken cancellationToken = default);

    /// <summary>
    /// 发布文件删除事件（在事务成功后调用）
    /// </summary>
    /// <param name="changes">变更列表</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task PublishDeleteEventsAsync(IReadOnlyList<FileReferenceChange> changes, CancellationToken cancellationToken = default);
}
