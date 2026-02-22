
namespace Tnzi.DependencyInjection;

/// <summary>
/// 依赖注入自动注册模块
/// </summary>
[DependsOn(typeof(CoreServicesModule))]
public class DependencyInjectionModule : TnziCoreModule
{
    public override int LoadOrder => 1;

    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        var options = context.Configuration
            .GetSection("Tnzi:DependencyInjection")
            .Get<DependencyRegistrationOptions>() ?? new DependencyRegistrationOptions();

        if (!options.Enabled)
            return Task.CompletedTask;

        var registrationService = new DependencyRegistrationService(context.Services, options);
        registrationService.RegisterAll();

        return Task.CompletedTask;
    }
}