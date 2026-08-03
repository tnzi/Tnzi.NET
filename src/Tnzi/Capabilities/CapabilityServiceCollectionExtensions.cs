namespace Tnzi.Capabilities;

/// <summary>
/// Service-collection entry points for capability declaration.
/// </summary>
/// <remarks>
/// Declaring happens during <c>ConfigureServicesAsync</c>, before any service provider exists, so
/// the catalog is registered as a <b>ready-made instance</b> rather than a type: modules need to
/// write into the very object the endpoint will later read, and there is no container yet to
/// resolve it from.
/// <para>
/// Both methods find-or-create that single instance, so declaration order between modules does not
/// matter and no module has to know whether another one got there first.
/// </para>
/// </remarks>
public static class CapabilityServiceCollectionExtensions
{
    /// <summary>
    /// Ensure the capability catalog is registered. Idempotent.
    /// </summary>
    public static IServiceCollection AddTnziCapabilities(this IServiceCollection services)
    {
        Check.NotNull(services);
        GetOrCreateCatalog(services);
        return services;
    }

    /// <summary>
    /// Declare a capability this server supports.
    /// </summary>
    /// <example>
    /// <code>
    /// context.Services.DeclareCapability(ChatCapabilities.DraftRestoreV1);
    /// </code>
    /// </example>
    /// <exception cref="ArgumentException">The name is not a well-formed capability name.</exception>
    public static IServiceCollection DeclareCapability(this IServiceCollection services, string capability)
    {
        Check.NotNull(services);
        GetOrCreateCatalog(services).Declare(capability);
        return services;
    }

    private static ICapabilityCatalog GetOrCreateCatalog(IServiceCollection services)
    {
        foreach (var descriptor in services)
        {
            if (descriptor.ServiceType == typeof(ICapabilityCatalog)
                && descriptor.ImplementationInstance is ICapabilityCatalog existing)
            {
                return existing;
            }
        }

        var catalog = new CapabilityCatalog();
        services.AddSingleton<ICapabilityCatalog>(catalog);
        return catalog;
    }
}
