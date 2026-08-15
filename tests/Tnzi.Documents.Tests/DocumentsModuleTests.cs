using Microsoft.Extensions.Configuration;
using Tnzi.Modules;

namespace Tnzi.Documents.Tests;

/// <summary>
/// <see cref="DocumentsModule"/> 的接线契约。
/// </summary>
/// <remarks>
/// 「三个原语都注册了、消费应用能整体覆盖、模块不带表前缀」这几条只体现在注册代码里，
/// 改错了编译照过，直到运行时才会发现。
/// </remarks>
public class DocumentsModuleTests
{
    [Theory]
    [InlineData(typeof(IPdfInspector), typeof(PdfPigPdfInspector))]
    [InlineData(typeof(IPdfStamper), typeof(PdfSharpPdfStamper))]
    public void Module_RegistersTheDefaultImplementationOfEachPrimitive(Type contract, Type implementation)
    {
        var services = ConfigureModule();

        var descriptor = services.Single(service => service.ServiceType == contract);
        descriptor.ImplementationType.ShouldBe(implementation);
        descriptor.Lifetime.ShouldBe(ServiceLifetime.Singleton);
    }

    /// <summary>
    /// 转换器是**一个**服务、背后两个引擎：HTML 交浏览器，其余交 LibreOffice。
    /// </summary>
    /// <remarks>
    /// 分流只体现在注册代码里，接错了编译照过 —— 症状是「PDF 出来了，只是排版全丢」。
    /// </remarks>
    [Fact]
    public async Task Module_RegistersOneConverterThatRoutesHtmlToTheBrowserAndTheRestToLibreOffice()
    {
        using var provider = await BuildProviderAsync();

        var converter = provider.GetRequiredService<IDocumentConverter>();

        converter.ShouldBeOfType<RoutingDocumentConverter>();
        converter.CanConvert("composed.html").ShouldBeTrue();
        converter.CanConvert("contract.docx").ShouldBeTrue();
        converter.CanConvert("photo.png").ShouldBeFalse();

        // 单例：两个引擎都无状态，且浏览器并发闸门挂在实例上，解析出两份会让上限翻倍
        provider.GetRequiredService<IDocumentConverter>().ShouldBeSameAs(converter);
    }

    [Fact]
    public void ConsumerRegisteredImplementation_WinsOverTheModuleDefault()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        // 消费应用先注册（模块用 TryAddSingleton，先注册者胜出）
        services.AddSingleton<IPdfStamper, StubStamper>();

        RunConfigure(services);

        services.Single(service => service.ServiceType == typeof(IPdfStamper))
            .ImplementationType.ShouldBe(typeof(StubStamper));
    }

    [Fact]
    public void Module_HasNoTableNamePrefix_BecauseItOwnsNoEntities()
    {
        // 无实体、无表 => TnziCustomModule（而不是带 TableNamePrefix 的 TnziApplicationModule）
        new DocumentsModule().ShouldBeAssignableTo<TnziCustomModule>();
        typeof(DocumentsModule).ShouldNotBeAssignableTo<IHasTableNamePrefix>();
    }

    [Fact]
    public void Module_DependsOnNothing_BecauseThePrimitivesAreBusinessAgnostic()
    {
        typeof(DocumentsModule)
            .GetCustomAttributes(typeof(DependsOnAttribute), inherit: false)
            .ShouldBeEmpty();
    }

    [Fact]
    public async Task Module_RegistersOptionsBoundToTheDocumentsSection()
    {
        using var provider = await BuildProviderAsync(new Dictionary<string, string?>
        {
            ["Documents:ConversionTimeoutSeconds"] = "45",
            ["Documents:Html:PaperSize"] = "Legal"
        });

        provider.GetRequiredService<IOptions<DocumentsOptions>>().Value.ConversionTimeoutSeconds.ShouldBe(45);
        provider.GetRequiredService<IOptions<HtmlPdfOptions>>().Value.PaperSize.ShouldBe("Legal");
    }

    /// <summary>
    /// <c>Documents:Html:Enabled = false</c> 是退回旧行为的唯一开关：浏览器引擎不再认领 HTML。
    /// </summary>
    [Fact]
    public async Task Module_WhenBrowserRenderingIsDisabled_HtmlStillConverts_ThroughLibreOffice()
    {
        using var provider = await BuildProviderAsync(new Dictionary<string, string?>
        {
            ["Documents:Html:Enabled"] = "false"
        });

        var converter = provider.GetRequiredService<IDocumentConverter>();

        // 仍然认领 .html —— 只是这次认领它的是 LibreOffice
        converter.CanConvert("composed.html").ShouldBeTrue();
        provider.GetRequiredService<ChromiumHtmlDocumentConverter>().CanConvert("composed.html").ShouldBeFalse();
    }

    private static async Task<ServiceProvider> BuildProviderAsync(Dictionary<string, string?>? settings = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings ?? [])
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();

        var module = new DocumentsModule();
        var context = new ServiceConfigurationContext(services, configuration);
        await module.PreConfigureServicesAsync(context);
        await module.ConfigureServicesAsync(context);

        return services.BuildServiceProvider();
    }

    private static ServiceCollection ConfigureModule()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        RunConfigure(services);
        return services;
    }

    private static void RunConfigure(ServiceCollection services)
    {
        var context = new ServiceConfigurationContext(services, new ConfigurationBuilder().Build());
        new DocumentsModule().ConfigureServicesAsync(context).GetAwaiter().GetResult();
    }

    private sealed class StubStamper : IPdfStamper
    {
        public byte[] Stamp(byte[] pdf, PdfStampRequest request) => pdf;

        public byte[] Create(PdfStampRequest request) => [];
    }
}
