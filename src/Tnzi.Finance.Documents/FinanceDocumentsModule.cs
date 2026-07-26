namespace Tnzi.Finance.Documents;

/// <summary>
/// Finance 文档渲染子模块：提供 <see cref="ICheckDocumentRenderer"/> 的两个实现
/// （模板驱动的 <see cref="TemplateCheckRenderer"/> 为默认，<see cref="PdfSharpCheckRenderer"/> 为备选），
/// 让 Finance 的支票打印（<c>admin/finance/checks</c> 的 print / preview / reprint / calibration）开箱即用。
/// </summary>
/// <remarks>
/// 可选。<see cref="ICheckDocumentRenderer"/> 契约留在 Finance 核心（核心刻意零渲染库引用——支票号分配、
/// 打印队列、登记、作废、毁票等生命周期与渲染无关），本子模块把渲染依赖（Razor 模板引擎、PdfSharp）
/// 隔离进来，纯会计消费者不再被传递拉入。加载本模块即绑定渲染实现；消费应用可先自注册
/// <see cref="ICheckDocumentRenderer"/> 覆盖（默认经 <c>TryAddScoped</c>，先注册者胜出），
/// 或在自己的模块里 <c>AddScoped&lt;ICheckDocumentRenderer, PdfSharpCheckRenderer&gt;()</c> 切回 PDF 路径。
/// 未加载时 <see cref="ICheckService"/> 的 print / preview / reprint / calibration 返回 501 引导，
/// 其余支票生命周期照常可用，与 <c>Tnzi.Finance.Ai</c> 的 <c>IReceiptExtractor</c> 501 兜底同构。
/// 无实体、无表，故用 <see cref="TnziCustomModule"/>，不设 <c>TableNamePrefix</c>。
/// </remarks>
[DependsOn(typeof(FinanceModule))]
[DependsOn(typeof(FinanceBankingModule))]
[DependsOn(typeof(TemplateModule))]
public class FinanceDocumentsModule : TnziCustomModule
{
    /// <inheritdoc />
    public override int LoadOrder => 58;

    /// <inheritdoc />
    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // 默认渲染器 = 模板驱动（HTML，所见即所得预览 + 浏览器打印）。
        // TryAddScoped 让消费应用先注册自己的渲染器即可覆盖。
        context.Services.TryAddScoped<ICheckDocumentRenderer, TemplateCheckRenderer>();
        // 对账单渲染同一范式：未加载本模块时 Finance 的对账单**数据**照常可取，
        // 只有出文档的端点返回 501 引导。
        context.Services.TryAddScoped<IStatementRenderer, TemplateStatementRenderer>();

        // PdfSharp 渲染器保留为可显式选用的备选（真 PDF / 白纸 E-13B 磁码字形）：
        // 以具体类型注册，消费应用只需 AddScoped<ICheckDocumentRenderer, PdfSharpCheckRenderer>() 即可切换。
        context.Services.TryAddScoped<PdfSharpCheckRenderer>();

        // 内置 CPA-006 支票模板的幂等播种（迁移之后执行，空库首启即可用；已存在则跳过不覆盖）。
        context.Services.AddTransient<IPostMigrationStartupTask, CheckTemplateSeeder>();

        return base.ConfigureServicesAsync(context);
    }
}
