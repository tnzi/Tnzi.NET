namespace Tnzi.Feature;

/// <summary>
/// SaaS Feature Toggle module.
/// Provides pluggable feature value providers (Tenant, Edition, etc.)
/// and a FeatureChecker for business code to query feature states.
/// </summary>
[DependsOn(typeof(EFCoreModule))]
public class FeatureModule : TnziApplicationModule
{
    /// <summary>
    /// Load after Authorization module
    /// </summary>
    public override int LoadOrder => 15;

    /// <summary>
    /// Table name prefix
    /// </summary>
    public override string? TableNamePrefix => "Feature";

    /// <summary>
    /// Pre-configure: register options and validators
    /// </summary>
    public override Task PreConfigureServicesAsync(ServiceConfigurationContext context)
    {
        context.Services.AddTnziOptions<FeatureOptions, FeatureOptionsValidator>(context.Configuration);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Configure services: manual registration (framework assembly)
    /// </summary>
    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // Code-declared permissions for this module's admin surfaces - the
        // Authorization module's PermissionDbSeeder picks every registered
        // provider up on startup (no-op when Authorization is not loaded).
        context.Services.AddTransient<IPermissionDefinitionProvider, FeaturePermissions>();

        var services = context.Services;

        // Register FeatureManager (singleton, manages immutable snapshot)
        services.AddSingleton<IFeatureManager, FeatureManager>();

        // Register FeatureChecker (scoped, evaluates provider chain per request)
        services.AddScoped<IFeatureChecker, FeatureChecker>();

        // Register FeatureService (scoped, admin CRUD)
        services.AddScoped<IFeatureService, FeatureService>();

        // Register FeatureUsageService (scoped, usage analytics)
        services.AddScoped<IFeatureUsageService, FeatureUsageService>();

        // Register built-in value providers
        // TenantFeatureValueProvider resolves feature values per tenant (priority 200)
        // Applications can register custom IFeatureValueProvider implementations
        // (e.g., EditionFeatureValueProvider for SaaS edition-based features)
        services.AddScoped<IFeatureValueProvider, TenantFeatureValueProvider>();

        return Task.CompletedTask;
    }
}
