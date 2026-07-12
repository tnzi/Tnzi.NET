using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Swashbuckle.AspNetCore.SwaggerGen;
using Tnzi.Modules;
using Tnzi.Swagger.Options;

namespace Tnzi.Swagger.Tests;

public class SwaggerModuleTests
{
    [Fact]
    public async Task ConfigureServicesAsync_WithEnabledSwagger_RegistersSwaggerServices()
    {
        var module = new SwaggerModule();
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Swagger:Enabled"] = "true",
                ["Swagger:DefaultDocument:Name"] = "default",
                ["Swagger:DefaultDocument:Title"] = "Default API",
                ["Swagger:DefaultDocument:Version"] = "v1",
            })
            .Build();

        var context = new ServiceConfigurationContext(services, configuration);
        await module.PreConfigureServicesAsync(context);
        await module.ConfigureServicesAsync(context);

        Assert.Equal(ModuleType.Framework, module.ModuleType);
        Assert.Equal(10, module.LoadOrder);
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IConfigureOptions<SwaggerGenOptions>));
    }

    [Fact]
    public async Task ConfigureServicesAsync_WithDisabledSwagger_SkipsSwaggerRegistration()
    {
        var module = new SwaggerModule();
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Swagger:Enabled"] = "false",
            })
            .Build();

        var context = new ServiceConfigurationContext(services, configuration);
        await module.PreConfigureServicesAsync(context);
        await module.ConfigureServicesAsync(context);

        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IConfigureOptions<SwaggerGenOptions>));
    }

    [Fact]
    public void BuildSchemaId_UnambiguousType_KeepsShortName()
    {
        var ambiguous = new HashSet<string>();

        Assert.Equal("SwaggerOptions", SwaggerModule.BuildSchemaId(typeof(SwaggerOptions), ambiguous));
    }

    [Fact]
    public void BuildSchemaId_AmbiguousType_QualifiesWithNamespace()
    {
        // 模拟跨模块同名 DTO（Tnzi.Payment.Dtos.InvoiceDto vs Tnzi.Finance.Dtos.InvoiceDto）
        var ambiguous = new HashSet<string> { nameof(SwaggerOptions) };

        // Tnzi.Swagger.SwaggerOptions → 去 Tnzi 前缀后 = SwaggerSwaggerOptions
        Assert.Equal("SwaggerSwaggerOptions", SwaggerModule.BuildSchemaId(typeof(SwaggerOptions), ambiguous));
    }

    [Fact]
    public void BuildSchemaId_GenericType_ConcatenatesArgsBeforeName()
    {
        var ambiguous = new HashSet<string>();

        // 与 Swashbuckle 既有默认形态一致：参数在前 + 去元数的泛型名
        Assert.Equal("SwaggerOptionsList", SwaggerModule.BuildSchemaId(typeof(List<SwaggerOptions>), ambiguous));
        Assert.Equal("StringSwaggerOptionsDictionary", SwaggerModule.BuildSchemaId(typeof(Dictionary<string, SwaggerOptions>), ambiguous));
    }

    [Fact]
    public void BuildSchemaId_GenericOverAmbiguousArg_DisambiguatesArg()
    {
        var ambiguous = new HashSet<string> { nameof(SwaggerOptions) };

        Assert.Equal("SwaggerSwaggerOptionsList", SwaggerModule.BuildSchemaId(typeof(List<SwaggerOptions>), ambiguous));
    }
}
