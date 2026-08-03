namespace Tnzi.Finance.Banking.Services;

/// <summary>
/// 「你有权看收据，就看得见收据的原始图片」。
///
/// 收据的照片通常是**上传者以外的人**在审：录入的是出差的同事，核对的是会计。
/// 后者既不是文件的创建者，也不该为了看一张收据照片而拿到 <c>storage.file.view</c>。
///
/// 判据复用收据自己的权限码 <c>finance.receipt.view</c>，与
/// <see cref="Tnzi.Finance.Services.FinanceFileReferenceAccessResolver"/> 同一路子：
/// 「能不能看这条记录」已经有答案了，文件跟着记录走。
/// </summary>
public class ReceiptFileReferenceAccessResolver : IFileReferenceAccessResolver
{
    private const string ReceiptViewPermission = "finance.receipt.view";

    private readonly IPermissionChecker? _permissionChecker;

    public ReceiptFileReferenceAccessResolver(IPermissionChecker? permissionChecker = null)
    {
        _permissionChecker = permissionChecker;
    }

    public bool CanHandle(string entityType) => entityType == nameof(Receipt);

    public async Task<bool> CanReadAsync(FileReferenceDescriptor reference, CancellationToken cancellationToken = default)
    {
        // 未加载 Authorization 模块时无从判定 —— 保守拒绝，让 Storage 自己的归属判据兜底。
        if (_permissionChecker == null)
            return false;

        return await _permissionChecker.IsGrantedAsync(ReceiptViewPermission);
    }
}
