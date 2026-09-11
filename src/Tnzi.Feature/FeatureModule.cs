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

        // Usage records leave the request path through a bounded in-memory queue and are
        // written in batches by a hosted service (same shape as the access log and the audit
        // pipeline). One sender instance serves both ends of the channel.
        services.AddSingleton<FeatureUsageSender>();
        services.AddSingleton<IFeatureUsageSender>(sp => sp.GetRequiredService<FeatureUsageSender>());
        services.AddSingleton<IFeatureUsageConsumer>(sp => sp.GetRequiredService<FeatureUsageSender>());
        services.AddHostedService<FeatureUsageBackgroundService>();

        // Resolved values are cached per scope (Feature:ValueCacheSeconds, 0 = off) and
        // invalidated exactly by the value-changed / value-deleted events the admin API
        // publishes after each successful write.
        services.AddSingleton<FeatureValueCache>();
        services.AddEventHandler<FeatureValueChangedEvent, FeatureValueCacheInvalidationHandler>();
        services.AddEventHandler<FeatureValueDeletedEvent, FeatureValueCacheInvalidationHandler>();

        // Register built-in value providers. The chain is evaluated by descending priority:
        //   Tenant (200) -> Global (100) -> definition default.
        // Global is what makes admin-written values take effect in a single-tenant deployment;
        // without it the only reader was tenant-scoped and every value written with
        // multi-tenancy off was accepted and then never evaluated.
        // Applications can register further IFeatureValueProvider implementations
        // (e.g. an edition provider for SaaS plans); the admin API only accepts values for
        // providers that are registered here.
        services.AddScoped<IFeatureValueProvider, TenantFeatureValueProvider>();
        services.AddScoped<IFeatureValueProvider, GlobalFeatureValueProvider>();

        return Task.CompletedTask;
    }
}
