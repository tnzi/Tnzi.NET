namespace Tnzi.Signing.Services;

/// <summary>
/// 「你有权看签署请求，就看得见它的成品与完成证书；有权看模板，就看得见模板的原件与渲染稿」。
/// </summary>
/// <remarks>
/// <para>
/// 为什么需要它：成品与完成证书是在<b>匿名的</b>最后一次提交里存下来的，<c>FileRecord.CreatorId</c>
/// 为空 —— 无主记录不视为任何人所有（<c>FileAccessAuthorizer.IsOwner</c> 恒假）。于是持
/// <c>signing.request.view</c> 的管理员在请求详情里看得到 <c>finalPdfFileId</c>，点下去却是 404，
/// 除非他另外持有 <c>storage.file.view</c> —— 那是整个文件库的管理权限，里面还躺着合同和 HR 文件。
/// 本模块此前没有向 Storage 登记任何按引用放行的判据（Chat / Finance 都登记了），所以判据 7
/// 对签署的文件一直是空转。
/// </para>
/// <para>
/// 判据直接复用本模块的权限码：<see cref="Envelope"/> 名下的文件（渲染稿 / 成品 / 完成证书）看
/// <c>signing.request.view</c>；<see cref="EnvelopeTemplate"/> 名下的文件（上传原件 / 渲染稿）看
/// <c>signing.template.view</c>。这两个码已经是「能不能看请求 / 模板」这件事的答案，在这里另立一套
/// 只会让两处慢慢漂移。刻意<b>不</b>逐请求判定可见性：本模块的请求可见性目前就是按权限码而不是按行的。
/// </para>
/// <para>
/// 只负责放行：返回 false 只表示「本解析器不放行」，Storage 自己的归属与权限码判据、以及匿名收件人
/// 走的请求级授予（<c>IFileAccessGrantContext</c>）都不受影响。
/// </para>
/// </remarks>
public class SigningFileReferenceAccessResolver : IFileReferenceAccessResolver
{
    private readonly IPermissionChecker? _permissionChecker;

    public SigningFileReferenceAccessResolver(IPermissionChecker? permissionChecker = null)
    {
        _permissionChecker = permissionChecker;
    }

    public bool CanHandle(string entityType)
        => entityType == nameof(Envelope) || entityType == nameof(EnvelopeTemplate);

    public async Task<bool> CanReadAsync(FileReferenceDescriptor reference, CancellationToken cancellationToken = default)
    {
        // 未加载 Authorization 模块时无从判定 —— 保守拒绝，让 Storage 自己的归属判据兜底
        // （与 FileAccessAuthorizer 里同样的取舍）。
        if (_permissionChecker == null)
            return false;

        var permission = reference.EntityType == nameof(EnvelopeTemplate)
            ? SigningPermissionNames.TemplateView
            : SigningPermissionNames.RequestView;

        return await _permissionChecker.IsGrantedAsync(permission);
    }
}
