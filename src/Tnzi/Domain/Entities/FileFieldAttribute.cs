
namespace Tnzi.Domain.Entities;

/// <summary>
/// 标记属性为文件字段，用于自动跟踪文件引用
/// </summary>
/// <remarks>
/// ★ 只在以 <c>Guid</c> 作 <c>Id</c> 的实体上受支持：文件引用表的实体标识是 Guid 列。
/// <c>long</c> / <c>int</c> / <c>string</c> 主键的实体保存文件字段时，Storage 模块的引用处理器抛
/// <see cref="InvalidOperationException"/> 并指名实体，保存失败。不能安静跳过：那会让文件以引用计数 0
/// 被孤儿清理物理删除，而实体行仍指着它。
/// </remarks>
[AttributeUsage(AttributeTargets.Property)]
public class FileFieldAttribute : Attribute
{
    /// <summary>
    /// 是否多文件字段（存储为 JSON 数组）
    /// </summary>
    public bool Multiple { get; set; } = false;

    /// <summary>
    /// 声明"被本字段引用的文件是有意公开的",典型是头像、站点素材这类要以匿名
    /// <c>&lt;img src="/api/files/{id}/download"&gt;</c> 形式消费的资源。
    ///
    /// 置 true 时,只要有文件 id 写进本字段,框架就在同一个事务里把对应的
    /// <c>FileRecord.IsPublic</c> 置 true(由 Storage 模块的文件引用处理器执行)。
    /// 意图声明在**字段**上,不在调用方手里——写头像的路径有很多条(个人中心 / 管理端 /
    /// OAuth 导入 / 消费应用自己的服务),任何一条忘记传参都会让头像变成 404。
    ///
    /// 只升不降:移除引用不会把文件改回私有(同一个文件可能仍被别处公开引用),
    /// 要收回公开须显式调用 <c>IFileStorageService.SetFileVisibilityAsync</c>。
    ///
    /// 默认 false。合同、支票、HR 文件这类字段**不要**打开它。
    /// </summary>
    public bool Public { get; set; } = false;
}