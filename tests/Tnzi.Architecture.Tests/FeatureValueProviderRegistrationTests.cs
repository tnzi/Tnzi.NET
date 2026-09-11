using Microsoft.Extensions.DependencyInjection;
using Tnzi.Feature.Services;

namespace Tnzi.Architecture.Tests;

/// <summary>
/// DI wiring gate for the Feature module's value-provider chain.
/// </summary>
/// <remarks>
/// <para>
/// Every unit test of <see cref="FeatureService"/> and <see cref="FeatureChecker"/> hands the
/// providers in by hand, so none of them can notice a provider that was written but never
/// registered in <c>FeatureModule.ConfigureServicesAsync</c>. That is exactly the failure this
/// batch fixes at the value level: a deployment with multi-tenancy off had the tenant provider
/// as its only reader, every admin-written value was accepted and then never evaluated.
/// Drop the <c>GlobalFeatureValueProvider</c> registration and that state returns with every
/// Feature unit test still green.
/// </para>
/// </remarks>
public class FeatureValueProviderRegistrationTests
{
    [Fact]
    public void FeatureModule_RegistersTenantAndGlobalProviders_Scoped()
    {
        var graph = ArchitectureModuleGraph.Load();

        var registrations = graph.FinalServices
            .Where(d => d.ServiceType == typeof(IFeatureValueProvider))
            .ToList();

        var implementations = registrations
            .Select(d => d.ImplementationType)
            .Where(t => t != null)
            .Select(t => t!)
            .ToList();

        Assert.Contains(typeof(TenantFeatureValueProvider), implementations);
        Assert.Contains(typeof(GlobalFeatureValueProvider), implementations);

        // Providers read the request-scoped repository; a singleton would capture one
        // DbContext for the process lifetime.
        Assert.All(registrations, d => Assert.Equal(ServiceLifetime.Scoped, d.Lifetime));

        // Names are the write-side contract (FeatureValue.ProviderName); two providers sharing
        // one would make the admin API's provider lookup ambiguous.
        Assert.NotEqual(TenantFeatureValueProvider.ProviderName, GlobalFeatureValueProvider.ProviderName);
    }
}
