using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Swashbuckle.AspNetCore.SwaggerGen;
using Tnzi.Modules;

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
}
