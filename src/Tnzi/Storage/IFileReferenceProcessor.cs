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
    /// 实体ID（字符串形式，追踪器按主键原样收集）。
    /// </summary>
    /// <remarks>
    /// Storage 模块的引用表只存得下 Guid 主键：主键是 long / int / string 的实体声明 <c>[FileField]</c>，
    /// 启动时被拒绝，运行时遇到漏网的同样拒绝保存，不会安静地不追踪。
    /// </remarks>
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
    /// 收尾解绑：这批变更里被解绑、已无人持有的文件，删掉记录并请求删除它的对象。
    /// </summary>
    /// <remarks>
    /// 在保存（含 <see cref="ProcessChangesAsync"/> 与随后的 flush）之后调用。外层工作单元开着时仍在它的事务里：
    /// 记录的删除随事务一起提交或回滚，删对象的请求经事务感知的事件总线在提交之后才执行。
    /// 实现不应让这一步的失败抛给调用方，业务实体此时已经保存成功。
    /// </remarks>
    /// <param name="changes">变更列表</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task PublishDeleteEventsAsync(IReadOnlyList<FileReferenceChange> changes, CancellationToken cancellationToken = default);
}
