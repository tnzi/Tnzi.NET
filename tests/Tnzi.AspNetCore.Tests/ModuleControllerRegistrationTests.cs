using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tnzi.Modules;

namespace Tnzi.AspNetCore.Tests;

[DependsOn(typeof(AspNetCoreModule))]
internal sealed class TestControllerRegistrationStartupModule : TnziApplicationModule
{
}

[ApiController]
[Route("module-controller-registration-test")]
internal sealed class TestModuleController : ApiControllerBase
{
    [HttpGet]
    public ApiResult<string> Get()
    {
        return Ok("ok", "Success");
    }
}

public class ModuleControllerRegistrationTests
{
    [Fact]
    public async Task AddTnziAsync_ShouldRegisterApplicationModuleAssemblyAsApplicationPart()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:AutoDiscoverDbContexts"] = "false"
            })
            .Build();

        await services.AddTnziAsync<TestControllerRegistrationStartupModule>(configuration);

        using var serviceProvider = services.BuildServiceProvider();
        var partManager = serviceProvider.GetRequiredService<ApplicationPartManager>();

        Assert.Contains(
            partManager.ApplicationParts,
            part => part.Name == typeof(TestControllerRegistrationStartupModule).Assembly.GetName().Name);
    }
}
