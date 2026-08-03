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

    /// <summary>
    /// 能否为这个文件**签发**访问令牌。
    ///
    /// 与 <see cref="CanReadAsync"/> 分开的唯一理由:签发不该认「签名令牌」这条判据。
    /// 否则一个 10 分钟的渲染凭据就能在到期前拿去换一张新的,无限续期 —— TTL 对任何
    /// 已登录的持有者都形同虚设,而 TTL 正是这套机制唯一的止损面。
    ///
    /// 默认实现等同读取权限;<see cref="FileAccessAuthorizer"/> 覆盖它以排除签名那一条。
    /// </summary>
    Task<bool> CanMintAccessTokenAsync(FileRecord record, CancellationToken cancellationToken = default)
        => CanReadAsync(record, cancellationToken);
}
