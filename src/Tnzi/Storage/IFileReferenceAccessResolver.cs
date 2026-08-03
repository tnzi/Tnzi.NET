namespace Tnzi.Storage;

/// <summary>
/// 一条文件引用的只读描述（谁引用了这个文件）。
///
/// 刻意用原始类型而不是 Storage 的 <c>FileReference</c> 实体：拥有业务记录的模块
/// （Chat / Finance / 消费应用）要能在**不引用 Tnzi.Storage** 的前提下实现
/// <see cref="IFileReferenceAccessResolver"/>，与 <see cref="FileReferenceChange"/>
/// 让 EFCore 不依赖 Storage 是同一条路子。
/// </summary>
/// <param name="FileId">被引用的文件 ID</param>
/// <param name="EntityType">引用方实体类型名（如 <c>ChatMessage</c>）</param>
/// <param name="EntityId">引用方实体 ID</param>
/// <param name="FieldName">引用方字段名（如 <c>FileId</c>）</param>
public readonly record struct FileReferenceDescriptor(
    Guid FileId,
    string EntityType,
    Guid EntityId,
    string FieldName);

/// <summary>
/// 按「引用它的那条记录」判定当前调用者能否读取一个文件。
///
/// 存在的理由：Storage 自己只认识「创建者」和「持 <c>storage.file.view</c> 的管理员」。
/// 可是聊天图片的**接收方**两样都不是，他却本来就该看得见那张图；财务附件的查看者
/// 同理。文件的可见性其实长在业务记录上，而只有拥有那条记录的模块说得清规则。
///
/// 契约：
/// <list type="bullet">
/// <item>只负责**放行**。返回 false 只表示「本解析器不放行」，不代表拒绝——
/// 其它解析器或 Storage 自身的归属/权限判据仍可能放行。</item>
/// <item>只在更便宜的判据（公开标记 / 签名令牌 / 归属 / 权限码）都不成立时才被调用，
/// 因此可以做数据库查询，但仍应保持单条查询量级。</item>
/// <item>可注册多个；任一放行即放行。未注册任何实现时该判据整体跳过（零查询）。</item>
/// </list>
/// </summary>
public interface IFileReferenceAccessResolver
{
    /// <summary>
    /// 本解析器是否负责该实体类型。为 false 时框架不会为这条引用调用
    /// <see cref="CanReadAsync"/>，据此避免无谓的查询。
    /// </summary>
    bool CanHandle(string entityType);

    /// <summary>
    /// 当前调用者能否读取「被这条引用指向的文件」。
    /// </summary>
    Task<bool> CanReadAsync(FileReferenceDescriptor reference, CancellationToken cancellationToken = default);
}
