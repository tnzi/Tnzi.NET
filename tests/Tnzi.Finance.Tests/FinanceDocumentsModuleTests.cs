using Tnzi.Finance.Documents;
using Tnzi.Finance.Documents.Services.Internal;
using Tnzi.Modules;
using Tnzi.Template;

namespace Tnzi.Finance.Tests;

/// <summary>
/// <see cref="FinanceDocumentsModule"/> 的渲染器接线契约
/// </summary>
/// <remarks>
/// 锁定「默认渲染器 = 模板驱动、PdfSharp 降级为可显式选用的备选、消费应用仍可整体覆盖」
/// 这三条契约——它们只体现在注册顺序里，改错了编译照过、直到打印出错才会发现。
/// </remarks>
public class FinanceDocumentsModuleTests
{
    [Fact]
    public void DefaultRenderer_IsTemplateDriven()
    {
        var services = ConfigureModule();

        var renderer = services.Single(d => d.ServiceType == typeof(ICheckDocumentRenderer));
        renderer.ImplementationType.ShouldBe(typeof(TemplateCheckRenderer));
        renderer.Lifetime.ShouldBe(ServiceLifetime.Scoped);
    }

    [Fact]
    public void PdfSharpRenderer_StaysAvailableAsAnExplicitAlternative()
    {
        var services = ConfigureModule();

        // 以具体类型注册：消费应用只需 AddScoped<ICheckDocumentRenderer, PdfSharpCheckRenderer>() 即可切回 PDF
        services.ShouldContain(d => d.ServiceType == typeof(PdfSharpCheckRenderer));
    }

    [Fact]
    public async Task ConsumerRegisteredRenderer_WinsOverTheModuleDefault()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        // 消费应用先注册（模块用 TryAddScoped，先注册者胜出）
        services.AddScoped<ICheckDocumentRenderer, PdfSharpCheckRenderer>();

        await new FinanceDocumentsModule()
            .ConfigureServicesAsync(new ServiceConfigurationContext(services, EmptyConfiguration()));

        var renderer = services.Single(d => d.ServiceType == typeof(ICheckDocumentRenderer));
        renderer.ImplementationType.ShouldBe(typeof(PdfSharpCheckRenderer));
    }

    [Fact]
    public void BuiltInCheckTemplate_IsSeededAfterMigrations()
    {
        var services = ConfigureModule();

        var seeder = services.Single(d => d.ServiceType == typeof(IPostMigrationStartupTask));
        seeder.ImplementationType.ShouldBe(typeof(CheckTemplateSeeder));
    }

    [Fact]
    public void Module_DependsOnFinanceAndTemplate()
    {
        var deps = typeof(FinanceDocumentsModule)
            .GetCustomAttributes(typeof(DependsOnAttribute), inherit: false)
            .Cast<DependsOnAttribute>()
            .SelectMany(a => a.DependedModuleTypes)
            .ToList();

        deps.ShouldContain(typeof(FinanceModule));
        // 模板驱动渲染要求模板存储与 Razor 引擎在场
        deps.ShouldContain(typeof(TemplateModule));
    }

    private static ServiceCollection ConfigureModule()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        new FinanceDocumentsModule()
            .ConfigureServicesAsync(new ServiceConfigurationContext(services, EmptyConfiguration()))
            .GetAwaiter().GetResult();
        return services;
    }

    private static IConfiguration EmptyConfiguration()
        => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
}
