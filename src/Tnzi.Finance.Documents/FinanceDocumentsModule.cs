namespace Tnzi.Finance.Documents;

/// <summary>
/// Finance 文档渲染子模块：提供默认的 <see cref="ICheckDocumentRenderer"/>（基于 PDFsharp 的支票 PDF 渲染），
/// 让 Finance 的支票打印（<c>admin/finance/checks</c> 的 print / reprint / calibration）开箱即用。
/// </summary>
/// <remarks>
/// 可选。<see cref="ICheckDocumentRenderer"/> 契约留在 Finance 核心（核心刻意零 PdfSharp 引用——支票号分配、
/// 打印队列、登记、作废、毁票等生命周期与渲染无关），本子模块把 PdfSharp 依赖与 <c>PdfSharpCheckRenderer</c>
/// 隔离进来，纯会计消费者不再被传递拉入 PdfSharp。加载本模块即绑定渲染实现；消费应用可先自注册
/// <see cref="ICheckDocumentRenderer"/> 覆盖（默认经 <c>TryAddScoped</c>，先注册者胜出）。未加载时
/// <see cref="ICheckService"/> 的 print / reprint / calibration 返回 501 引导，其余支票生命周期照常可用，
/// 与 <c>Tnzi.Finance.Ai</c> 的 <c>IReceiptExtractor</c> 501 兜底同构。无实体、无表，故用 <see cref="TnziCustomModule"/>，
/// 不设 <c>TableNamePrefix</c>。
/// </remarks>
[DependsOn(typeof(FinanceModule))]
public class FinanceDocumentsModule : TnziCustomModule
{
    /// <inheritdoc />
    public override int LoadOrder => 57;

    /// <inheritdoc />
    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // Finance 可选的 ICheckDocumentRenderer 默认实现（PDFsharp 6.x，纯托管 MIT）。
        // TryAddScoped 让消费应用先注册自己的渲染器即可覆盖。
        context.Services.TryAddScoped<ICheckDocumentRenderer, PdfSharpCheckRenderer>();
        return base.ConfigureServicesAsync(context);
    }
}
