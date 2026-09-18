namespace Tnzi.Payment.Billing.Services;

/// <summary>
/// 回答存储模块「按这条发票记录看，当前用户能不能读它引用的文件」：发票的归属用户，或持
/// <c>payment.invoice.view</c> 的人。
/// </summary>
/// <remarks>
/// <para>
/// 发票产物经 <c>SaveWithReferenceAsync(…, nameof(Invoice), invoice.Id, nameof(Invoice.PdfFileId))</c>
/// 落进 Storage，多数时候是在 webhook / 后台里生成的，<c>FileRecord.CreatorId</c> 为 null ——
/// 没有这个解析器，文件在存储侧对客户是**私密且无主**的：客户下载自己的发票（<c>GET /invoices/{id}/pdf</c>
/// 走 <c>IFileStorageService.GetAsync</c>）会被判 404；客户自己重发发票时，通知模块按
/// <see cref="IFileReadAccessProbe"/> 问「这个人读得到这份文件吗」也会被答否。
/// </para>
/// <para>
/// 契约在核心 <c>Tnzi/Storage/</c>，本模块不引用 <c>Tnzi.Storage</c> 也能回答；未加载存储模块时没人来问，
/// 注册它是无害的（与 Chat 的同名解析器同一条路子）。
/// </para>
/// </remarks>
public class InvoiceFileReferenceAccessResolver : IFileReferenceAccessResolver
{
    /// <summary>与 <c>DefaultInvoiceAdminController</c> 类级门逐字相同的码：管理员能看发票就能看它的产物。</summary>
    private const string InvoiceViewPermission = "payment.invoice.view";

    private readonly ICurrentUser _currentUser;
    private readonly IReadOnlyRepository<Invoice, Guid> _invoices;
    private readonly IPermissionChecker? _permissionChecker;

    public InvoiceFileReferenceAccessResolver(
        ICurrentUser currentUser,
        IReadOnlyRepository<Invoice, Guid> invoices,
        IPermissionChecker? permissionChecker = null)
    {
        _currentUser = Check.NotNull(currentUser);
        _invoices = Check.NotNull(invoices);
        _permissionChecker = permissionChecker;
    }

    public bool CanHandle(string entityType) => entityType == nameof(Invoice);

    public async Task<bool> CanReadAsync(FileReferenceDescriptor reference, CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.Id;
        if (userId is null || userId == Guid.Empty)
            return false;

        var invoice = await _invoices.FirstOrDefaultAsync(i => i.Id == reference.EntityId, cancellationToken);
        if (invoice == null)
            return false;

        if (invoice.UserId == userId)
            return true;

        return _permissionChecker != null
               && await _permissionChecker.IsGrantedAsync(InvoiceViewPermission);
    }
}
