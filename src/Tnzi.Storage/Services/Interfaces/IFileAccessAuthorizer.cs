namespace Tnzi.Storage.Services;

/// <summary>
/// 判定当前调用者能否读取 / 变更某个文件记录。
///
/// 存在的理由:文件端点按 GUID 取资源,而框架的实体 ID 是**顺序 GUID**,可预测性远高于
/// 随机 GUID。仅凭"知道 id"不足以构成授权,否则整个库的文件都可被枚举。
///
/// 判定必须落在**服务层**而非控制器:`DefaultStorageController` 是 `[DefaultController]`,
/// 消费方可以在同路由注册自己的控制器把它整个替换掉——那样挂在控制器上的任何特性都随之失效。
/// 服务层是唯一所有调用路径都会经过的地方。
///
/// 消费方可注册自己的实现覆盖默认策略(模块用 TryAdd 注册)。
/// </summary>
public interface IFileAccessAuthorizer
{
    /// <summary>
    /// 能否读取(下载 / 预览 / 缩略图 / 读元数据)。
    /// </summary>
    Task<bool> CanReadAsync(FileRecord record, CancellationToken cancellationToken = default);

    /// <summary>
    /// 能否变更(删除 / 改标签或元数据 / 建版本 / 建分享链接)。
    /// </summary>
    Task<bool> CanWriteAsync(FileRecord record, CancellationToken cancellationToken = default);
}
