namespace Tnzi.Storage.Services;

/// <summary>
/// 默认的文件访问策略:归属优先,管理员按权限码放行,匿名读需显式开启。
///
/// 读取(自上而下,任一成立即放行):
///   1. `FileRecord.IsPublic` 为 true(头像 / 站点素材这类有意公开的资源)
///   2. `Storage:AllowAnonymousRead` 为 true(部署级开关,默认关闭)
///   3. 请求带着**对这个文件有效且未过期的签名令牌**(见 <see cref="IFileUrlSigner"/>)
///   4. 本次请求已被别的凭据授权(<see cref="IFileAccessGrantContext"/>,目前是分享令牌)
///   5. 调用者是创建者
///   6. 调用者持有 `storage.file.view`(管理端)
///   7. 任一 <see cref="IFileReferenceAccessResolver"/> 按引用它的业务记录放行
///      (聊天图片的接收方 / 财务附件的查看者:他们既不是创建者也没有存储权限码,
///       但本来就该看得见)
///
/// 第 3、4 条是**请求级凭据**:它们证明的是"这一次请求被允许",不是"这个人被允许"。
/// 签发令牌(<see cref="CanMintAccessTokenAsync"/>)因此跳过这两条 —— 否则一条限次数、
/// 会过期的分享链接就能被换成一张不受这些约束的令牌,渲染凭据也能无限自我续期。
///
/// 变更:创建者,或持有 `storage.file.update`。**不接受**匿名,也**不接受**签名令牌
/// ——令牌是为了让浏览器渲染得出图片,不是授权凭据;拿到一张图的渲染令牌不该能删掉它。
///
/// 对外分享(`FileShare`)走第 4 条:`FileShareService` 校验令牌/口令/过期/次数之后,
/// 把结论放进 <see cref="IFileAccessGrantContext"/>,本判定据此放行。校验逻辑留在服务层,
/// 控制器只负责把请求送进来。
/// </summary>
public class FileAccessAuthorizer : IFileAccessAuthorizer
{
    private readonly ICurrentUser _currentUser;
    private readonly IOptionsMonitor<StorageOptions> _optionsMonitor;
    private readonly IRepository<FileReference, Guid> _referenceRepository;
    private readonly IReadOnlyList<IFileReferenceAccessResolver> _referenceResolvers;
    private readonly IFileAccessGrantContext _grantContext;
    private readonly IPermissionChecker? _permissionChecker;
    private readonly IFileUrlSigner? _urlSigner;
    private readonly IHttpContextAccessor? _httpContextAccessor;

    /// <summary>
    /// 引用判据的每请求记忆。列表页会对同一批文件反复问同一个问题,而这条判据是
    /// 唯一要查库的;作用域内缓存把它压回每个文件一次。
    /// </summary>
    private readonly Dictionary<Guid, bool> _referenceVerdicts = new();

    private StorageOptions Options => _optionsMonitor.CurrentValue;

    public FileAccessAuthorizer(
        ICurrentUser currentUser,
        IOptionsMonitor<StorageOptions> optionsMonitor,
        IRepository<FileReference, Guid> referenceRepository,
        IEnumerable<IFileReferenceAccessResolver> referenceResolvers,
        IFileAccessGrantContext grantContext,
        IPermissionChecker? permissionChecker = null,
        IFileUrlSigner? urlSigner = null,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        _currentUser = Check.NotNull(currentUser);
        _optionsMonitor = Check.NotNull(optionsMonitor);
        _referenceRepository = Check.NotNull(referenceRepository);
        _referenceResolvers = Check.NotNull(referenceResolvers).ToList();
        _grantContext = Check.NotNull(grantContext);
        _permissionChecker = permissionChecker;
        _urlSigner = urlSigner;
        _httpContextAccessor = httpContextAccessor;
    }

    public Task<bool> CanReadAsync(FileRecord record, CancellationToken cancellationToken = default)
        => EvaluateReadAsync(record, allowRequestCredentials: true, cancellationToken);

    /// <summary>
    /// 签发令牌走的是**同一条读取判据,但不认请求级凭据**(签名令牌 / 分享授予)。
    /// 否则一个 10 分钟的渲染凭据能在到期前换一张新的、无限续期;一条限次数会过期的
    /// 分享链接也能被换成一张不受这些约束的令牌。
    /// </summary>
    public Task<bool> CanMintAccessTokenAsync(FileRecord record, CancellationToken cancellationToken = default)
        => EvaluateReadAsync(record, allowRequestCredentials: false, cancellationToken);

    private async Task<bool> EvaluateReadAsync(FileRecord record, bool allowRequestCredentials, CancellationToken cancellationToken)
    {
        Check.NotNull(record);

        if (record.IsPublic || Options.AllowAnonymousRead)
            return true;

        // 请求级凭据先于认证判定:带签名的 <img> 请求与分享链接访客本来就是匿名的。
        if (allowRequestCredentials && (HasValidSignature(record.Id) || _grantContext.IsGranted(record.Id)))
            return true;

        if (!_currentUser.IsAuthenticated)
            return false;

        if (IsOwner(record))
            return true;

        if (await HasPermissionAsync(StoragePermissionNames.FileView))
            return true;

        return await IsReadableByReferenceAsync(record.Id, cancellationToken);
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
    /// 当前请求的查询参数里是否带着对**这个**文件有效的签名令牌。
    ///
    /// 判定放在这里而不是控制器或中间件:控制器是 `[DefaultController]`,消费方可以整体
    /// 替换掉;中间件则只覆盖 HTTP 管线,绕不过服务层的其它调用路径。服务层是唯一必经处。
    /// </summary>
    private bool HasValidSignature(Guid fileId)
    {
        if (_urlSigner == null)
            return false;

        var request = _httpContextAccessor?.HttpContext?.Request;
        if (request == null)
            return false;

        var token = request.Query[IFileUrlSigner.QueryParameterName].FirstOrDefault();
        return !string.IsNullOrEmpty(token) && _urlSigner.TryValidate(fileId, token, out _);
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

    /// <summary>
    /// 问一遍「引用这个文件的业务记录」:文件的可见性常常长在那条记录上,而只有拥有
    /// 那条记录的模块说得清规则(聊天看会话成员,财务看单据权限码)。
    ///
    /// 没有注册任何解析器时整条判据跳过,一次查询都不发。
    /// </summary>
    private async Task<bool> IsReadableByReferenceAsync(Guid fileId, CancellationToken cancellationToken)
    {
        if (_referenceResolvers.Count == 0)
            return false;

        if (_referenceVerdicts.TryGetValue(fileId, out var cached))
            return cached;

        var verdict = await ResolveByReferenceAsync(fileId, cancellationToken);
        _referenceVerdicts[fileId] = verdict;
        return verdict;
    }

    private async Task<bool> ResolveByReferenceAsync(Guid fileId, CancellationToken cancellationToken)
    {
        var references = await _referenceRepository
            .ToListAsync(r => r.FileId == fileId, cancellationToken);

        if (references.Count == 0)
            return false;

        foreach (var reference in references)
        {
            var descriptor = new FileReferenceDescriptor(
                reference.FileId, reference.EntityType, reference.EntityId, reference.FieldName);

            foreach (var resolver in _referenceResolvers)
            {
                if (!resolver.CanHandle(reference.EntityType))
                    continue;

                if (await resolver.CanReadAsync(descriptor, cancellationToken))
                    return true;
            }
        }

        return false;
    }
}
