using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tnzi.Caching;
using Tnzi.Modules;
using Tnzi.Template;
using Tnzi.Template.Services;
using Xunit.Abstractions;

namespace Tnzi.Documents.Tests;

/// <summary>
/// 文档与模板两个模块都加载时，模板渲染出的票据（发票、收据、贷项通知单）经本包的 Chromium 转换器出真正的 PDF。
/// </summary>
/// <remarks>
/// 消费方实测：两个模块都在，发票下载却答 <c>200 text/html</c>。这里把两个模块按真实注册装在一起，
/// 在装了浏览器的机器上转一份真的 HTML；没有浏览器时转换器答不上「HTML 可转」，模板模块退回 HTML 直出，
/// 这条测试只核对那个退路并在输出里写明跳过了真实转换（与本项目其它浏览器测试同一个做法）。
/// </remarks>
public class TemplatePdfConversionTests(ITestOutputHelper output)
{
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var context = new ServiceConfigurationContext(services, new ConfigurationBuilder().Build());

        var caching = new CachingModule();
        var template = new TemplateModule();
        var documents = new DocumentsModule();
        foreach (var module in new ITnziModule[] { caching, template, documents })
            module.PreConfigureServicesAsync(context).GetAwaiter().GetResult();
        foreach (var module in new ITnziModule[] { caching, template, documents })
            module.ConfigureServicesAsync(context).GetAwaiter().GetResult();

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task WithBothModules_ARenderedReceiptBecomesARealPdf()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var converter = scope.ServiceProvider.GetRequiredService<IHtmlToPdfConverter>();

        ChromiumLocator.ResetCache();
        if (ChromiumLocator.Resolve(null) == null)
        {
            output.WriteLine("Skipped the real conversion: no Chromium-based browser is installed on this machine.");
            converter.ShouldBeOfType<HtmlPassThroughConverter>("without a browser the receipts stay HTML, as before");
            return;
        }

        converter.ShouldBeOfType<DocumentConverterHtmlToPdfConverter>();
        converter.ContentType.ShouldBe("application/pdf");
        converter.FileExtension.ShouldBe(".pdf");

        var pdf = await converter.ConvertAsync(
            "<!doctype html><html><head><meta charset=\"utf-8\"></head><body><h1>Receipt R-1</h1><p>Total Paid CA$113.00</p></body></html>");

        Encoding.ASCII.GetString(pdf, 0, 5).ShouldBe("%PDF-");
        var text = new PdfPigPdfInspector().GetInfo(pdf);
        text.PageCount.ShouldBe(1);
    }
}
