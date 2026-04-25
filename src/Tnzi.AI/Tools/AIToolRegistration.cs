namespace Tnzi.AI.Tools;

/// <summary>
/// Helper for AI sub-modules (Coder/Sandbox/Device/...) to register their tools.
/// </summary>
/// <remarks>
/// AIModule's tool scanner intentionally skips all <c>Tnzi.*</c> assemblies (see
/// <c>AIModule.OnApplicationInitializationAsync</c> + <c>IsFrameworkCoreAssembly</c>),
/// so each sub-module must register its own tools during application initialization.
/// </remarks>
public static class AIToolRegistration
{
    /// <summary>
    /// Scans <paramref name="assembly"/> for AI tools and registers them into
    /// <see cref="IToolRegistry"/>. Returns the number of tools registered.
    /// Logs a warning on failure (does not throw) so a malformed module cannot abort startup.
    /// </summary>
    public static int ScanAndRegisterAITools(IServiceProvider serviceProvider, Assembly assembly, ILogger logger)
    {
        Check.NotNull(serviceProvider);
        Check.NotNull(assembly);
        Check.NotNull(logger);

        var toolScanner = serviceProvider.GetService<IToolScanner>();
        var toolRegistry = serviceProvider.GetService<IToolRegistry>();
        if (toolScanner is null || toolRegistry is null)
        {
            return 0;
        }

        var count = 0;
        try
        {
            foreach (var tool in toolScanner.ScanAssembly(assembly))
            {
                toolRegistry.Register(tool);
                count++;
            }
            logger.LogInformation("Registered {Count} AI tools from {Assembly}", count, assembly.GetName().Name);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to scan and register AI tools from {Assembly}", assembly.GetName().Name);
        }

        return count;
    }
}
