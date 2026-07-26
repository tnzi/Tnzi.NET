namespace Tnzi.Storage.Services;

/// <summary>
/// 默认的文件访问策略:归属优先,管理员按权限码放行,匿名读需显式开启。
///
/// 读取:
///   1. `FileRecord.IsPublic` 为 true(头像 / 站点素材这类有意公开的资源)
///   2. `Storage:AllowAnonymousRead` 为 true(部署级开关,默认关闭)
///   3. 调用者是创建者
///   4. 调用者持有 `storage.file.view`(管理端)
///
/// 变更:创建者,或持有 `storage.file.update`。**不接受**匿名。
///
/// 私密文件的对外分发走 `FileShare`(token + 可选密码 + 次数上限 + 过期),
/// 那条路径不经过本判定——分享链接本身就是授权凭据。
/// </summary>
public class FileAccessAuthorizer : IFileAccessAuthorizer
{
    private readonly ICurrentUser _currentUser;
    private readonly IOptionsMonitor<StorageOptions> _optionsMonitor;
    private readonly IPermissionChecker? _permissionChecker;

    private StorageOptions Options => _optionsMonitor.CurrentValue;

    public FileAccessAuthorizer(
        ICurrentUser currentUser,
        IOptionsMonitor<StorageOptions> optionsMonitor,
        IPermissionChecker? permissionChecker = null)
    {
        _currentUser = Check.NotNull(currentUser);
        _optionsMonitor = Check.NotNull(optionsMonitor);
        _permissionChecker = permissionChecker;
    }

    public async Task<bool> CanReadAsync(FileRecord record, CancellationToken cancellationToken = default)
    {
        Check.NotNull(record);

        if (record.IsPublic || Options.AllowAnonymousRead)
            return true;

        if (!_currentUser.IsAuthenticated)
            return false;

        if (IsOwner(record))
            return true;

        return await HasPermissionAsync(StoragePermissionNames.FileView);
    }

    public async Task<bool> CanWriteAsync(FileRecord record, CancellationToken cancellationToken = default)
    {
        Check.NotNull(record);

        // A public flag makes a file readable, never writable: anyone could
        // otherwise delete the site's shared assets.
        if (!_currentUser.IsAuthenticated)
            return false;

        if (IsOwner(record))
            return true;

        return await HasPermissionAsync(StoragePermissionNames.FileUpdate);
    }

    /// <summary>
    /// 归属判定。`CreatorId` 为 null 的记录(后台任务 / 迁移数据产生)不视为任何人所有,
    /// 只能由持权限码的管理员访问——比"无主即公开"保守。
    /// </summary>
    private bool IsOwner(FileRecord record)
        => record.CreatorId.HasValue && _currentUser.Id.HasValue && record.CreatorId.Value == _currentUser.Id.Value;

    private async Task<bool> HasPermissionAsync(string permissionName)
    {
        // 未加载 Authorization 模块时没有 IPermissionChecker。此时保守拒绝:
        // 没有权限体系的部署里,"归属"是唯一可信的判据。
        if (_permissionChecker == null)
            return false;

        return await _permissionChecker.IsGrantedAsync(permissionName);
    }
}
