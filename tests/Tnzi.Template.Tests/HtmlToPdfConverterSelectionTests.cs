using System.Text;
using Microsoft.Extensions.Configuration;
using Tnzi.Caching;
using Tnzi.Documents;
using Tnzi.Modules;
using Tnzi.Template.Services;

namespace Tnzi.Template.Tests;

/// <summary>
/// 模板模块的 HTML → PDF 转换器：宿主有能把 HTML 转成 PDF 的核心 <see cref="IDocumentConverter"/> 就用它，否则 HTML 直出。
/// </summary>
/// <remarks>
/// 消费方实测：同时加载了模板与文档两个模块，发票下载却答 <c>200 text/html</c> —— 模板模块只注册了直出转换器，
/// 没有任何东西把文档模块现成的 Chromium 转换器接进来。
/// </remarks>
public class HtmlToPdfConverterSelectionTests
{
    private static ServiceProvider BuildProvider(Action<IServiceCollection>? before = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        before?.Invoke(services);
        var context = new ServiceConfigurationContext(services, new ConfigurationBuilder().Build());

        var caching = new CachingModule();
        var template = new TemplateModule();
        caching.PreConfigureServicesAsync(context).GetAwaiter().GetResult();
        template.PreConfigureServicesAsync(context).GetAwaiter().GetResult();
        caching.ConfigureServicesAsync(context).GetAwaiter().GetResult();
        template.ConfigureServicesAsync(context).GetAwaiter().GetResult();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void WithAnHtmlCapableDocumentConverter_TheTemplateModuleProducesPdf()
    {
        var documents = new FakeDocumentConverter(available: true);
        using var provider = BuildProvider(s => s.AddSingleton<IDocumentConverter>(documents));
        using var scope = provider.CreateScope();

        var converter = scope.ServiceProvider.GetRequiredService<IHtmlToPdfConverter>();

        Assert.IsType<DocumentConverterHtmlToPdfConverter>(converter);
        Assert.Equal("application/pdf", converter.ContentType);
        Assert.Equal(".pdf", converter.FileExtension);
    }

    [Fact]
    public void WithoutADocumentConverter_TheHtmlIsPassedThrough_AsBefore()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var converter = scope.ServiceProvider.GetRequiredService<IHtmlToPdfConverter>();

        Assert.IsType<HtmlPassThroughConverter>(converter);
        Assert.Equal("text/html", converter.ContentType);
    }

    [Fact]
    public void WhenTheDocumentConverterCannotDoHtmlHere_TheHtmlIsPassedThrough()
    {
        // 文档模块加载了，但这台机器没有浏览器：它答不上「HTML 可转」
        using var provider = BuildProvider(s => s.AddSingleton<IDocumentConverter>(new FakeDocumentConverter(available: false)));
        using var scope = provider.CreateScope();

        Assert.IsType<HtmlPassThroughConverter>(scope.ServiceProvider.GetRequiredService<IHtmlToPdfConverter>());
    }

    [Fact]
    public void AnApplicationsOwnConverter_RegisteredFirst_StillWins()
    {
        var own = new Mock<IHtmlToPdfConverter>().Object;
        using var provider = BuildProvider(s =>
        {
            s.AddScoped(_ => own);
            s.AddSingleton<IDocumentConverter>(new FakeDocumentConverter(available: true));
        });
        using var scope = provider.CreateScope();

        Assert.Same(own, scope.ServiceProvider.GetRequiredService<IHtmlToPdfConverter>());
    }

    [Fact]
    public async Task TheAdapter_HandsTheHtmlToTheDocumentConverterAsAnHtmlFile()
    {
        var documents = new FakeDocumentConverter(available: true);
        var converter = new DocumentConverterHtmlToPdfConverter(documents);

        var pdf = await converter.ConvertAsync("<p>Receipt R-1</p>");

        Assert.Equal("%PDF-fake", Encoding.ASCII.GetString(pdf));
        Assert.Equal("<p>Receipt R-1</p>", Encoding.UTF8.GetString(documents.LastSource!));
        Assert.EndsWith(".html", documents.LastFileName);
    }

    private sealed class FakeDocumentConverter(bool available) : IDocumentConverter
    {
        public byte[]? LastSource { get; private set; }
        public string? LastFileName { get; private set; }

        public bool IsAvailable => available;

        public bool CanConvert(string fileName) => fileName.EndsWith(".html", StringComparison.OrdinalIgnoreCase);

        public Task<byte[]> ConvertToPdfAsync(byte[] source, string sourceFileName, CancellationToken ct = default)
        {
            LastSource = source;
            LastFileName = sourceFileName;
            return Task.FromResult(Encoding.ASCII.GetBytes("%PDF-fake"));
        }
    }
}
