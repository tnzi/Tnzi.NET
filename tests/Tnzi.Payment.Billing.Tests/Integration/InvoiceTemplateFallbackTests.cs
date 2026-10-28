using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Tnzi.Template.Options;
using Tnzi.Template.Services;
using TemplateEntity = Tnzi.Template.Entities.Template;

namespace Tnzi.Payment.Billing.Tests.Integration;

/// <summary>
/// 发票模板渲染出错时落到内置 HTML，而不是让生成发票文件的调用以异常结束。
/// </summary>
/// <remarks>
/// 发票模板是可编辑内容，一处空引用就是渲染期错误。此前 <c>ITemplateRenderService</c> 把引擎异常原样抛出，
/// 发票服务按 <c>Succeeded</c> 写的内置 HTML 兜底只在「模板不存在」时走得到。这里用真的渲染服务与引擎，
/// 模板库里只有一条渲染期必然出错的发票模板。
/// </remarks>
public class InvoiceTemplateFallbackTests : BillingIntegrationTestBase
{
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 100 });

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        var store = new Mock<ITemplateStoreService>();
        store
            .Setup(s => s.GetTemplateAsync("InvoiceDefault", "Payment", "Invoice", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TemplateEntity>.Success(new TemplateEntity
            {
                TemplateName = "InvoiceDefault",
                Module = "Payment",
                Category = "Invoice",
                // 模型上没有 Customer，SafeDynamicObject 给 null，再取成员就是渲染期的空引用绑定
                ContentTemplate = "<h1>@Model.Customer.Name</h1>",
                IsActive = true
            }));

        var engine = new RazorTemplateEngine(
            Microsoft.Extensions.Options.Options.Create(new TemplateOptions { TemplateRootPath = Path.GetTempPath() }),
            NullLogger<RazorTemplateEngine>.Instance,
            _cache);

        services.AddScoped<ITemplateRenderService>(sp => new TemplateRenderService(sp, engine, store.Object));
    }

    public override void Dispose()
    {
        _cache.Dispose();
        base.Dispose();
    }

    [Fact]
    public async Task GeneratePdf_FallsBackToTheBuiltInHtml_WhenTheTemplateFailsToRender()
    {
        var created = await InScopeAsync<IPaymentInvoiceService, Result<PaymentInvoiceDto>>(s => s.CreateManualAsync(new CreatePaymentInvoiceDto
        {
            CustomerName = "Ada",
            CustomerEmail = "ada@example.com",
            InvoiceDate = DateTime.UtcNow,
            LineItems = [new InvoiceLineItemDto { Description = "Work", Quantity = 2, UnitPrice = 30m }]
        }));
        created.Succeeded.ShouldBeTrue(created.Message);

        var generated = await InScopeAsync<IPaymentInvoiceService, Result<string>>(s => s.GeneratePdfAsync(created.Data!.Id));

        generated.Succeeded.ShouldBeTrue(generated.Message);
        var invoice = (await ReloadAsync<Invoice>(created.Data!.Id))!;
        var html = await File.ReadAllTextAsync(invoice.PdfFilePath!);
        html.ShouldContain($"Invoice #{invoice.InvoiceNo}");
        html.ShouldContain("Ada");
    }
}
