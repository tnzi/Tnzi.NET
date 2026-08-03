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
    [InlineData(typeof(IDocumentConverter), typeof(LibreOfficeDocumentConverter))]
    [InlineData(typeof(IPdfInspector), typeof(PdfPigPdfInspector))]
    [InlineData(typeof(IPdfStamper), typeof(PdfSharpPdfStamper))]
    public void Module_RegistersTheDefaultImplementationOfEachPrimitive(Type contract, Type implementation)
    {
        var services = ConfigureModule();

        var descriptor = services.Single(service => service.ServiceType == contract);
        descriptor.ImplementationType.ShouldBe(implementation);
        descriptor.Lifetime.ShouldBe(ServiceLifetime.Singleton);
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
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Documents:ConversionTimeoutSeconds"] = "45" })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        var module = new DocumentsModule();
        await module.PreConfigureServicesAsync(new ServiceConfigurationContext(services, configuration));
        await module.ConfigureServicesAsync(new ServiceConfigurationContext(services, configuration));

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IOptions<DocumentsOptions>>().Value.ConversionTimeoutSeconds.ShouldBe(45);
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
