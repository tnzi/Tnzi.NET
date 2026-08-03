namespace Tnzi.Finance.Services;

/// <summary>
/// 「你有权看单据附件，就看得见附件本身」。
///
/// 为什么需要它：挂在发票 / 账单 / 费用上的凭据是**别人**上传的（供应商发来的 PDF、
/// 同事拍的收据），查看它的会计既不是文件的创建者，也不该为了看一张凭据而拿到
/// <c>storage.file.view</c>（那是整个文件库的管理权限，里面还躺着合同和 HR 文件）。
///
/// 判据直接复用附件自己的权限码 <c>finance.attachment.view</c> —— 它已经是"能不能看
/// 单据附件"这件事的答案，在这里另立一套只会让两处慢慢漂移。
///
/// 刻意**不**逐单据判定可见性：Finance 的单据可见性目前就是按权限码而不是按行的，
/// 在这里凭空加一层行级判据，会让附件比它挂着的单据本身更难看到。行级数据范围一旦
/// 落到 Finance 单据上，这里跟着改即可。
/// </summary>
public class FinanceFileReferenceAccessResolver : IFileReferenceAccessResolver
{
    private const string AttachmentViewPermission = "finance.attachment.view";

    private readonly IPermissionChecker? _permissionChecker;

    public FinanceFileReferenceAccessResolver(IPermissionChecker? permissionChecker = null)
    {
        _permissionChecker = permissionChecker;
    }

    public bool CanHandle(string entityType) => entityType == nameof(DocumentAttachment);

    public async Task<bool> CanReadAsync(FileReferenceDescriptor reference, CancellationToken cancellationToken = default)
    {
        // 未加载 Authorization 模块时无从判定 —— 保守拒绝，让 Storage 自己的
        // 归属判据兜底（与 FileAccessAuthorizer 里同样的取舍）。
        if (_permissionChecker == null)
            return false;

        return await _permissionChecker.IsGrantedAsync(AttachmentViewPermission);
    }
}
