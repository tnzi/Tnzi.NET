namespace Tnzi.AI.Tools;

/// <summary>
/// Helper for AI modules (engine + sub-modules like Sandbox) to register their tools.
/// </summary>
/// <remarks>
/// AIModule's tool scanner (<c>AIModule.OnApplicationInitializationAsync</c>) intentionally
/// skips all <c>Tnzi.*</c> assemblies, so each sub-module must register its own tools during
/// application initialization. <see cref="IToolScanner"/>/<see cref="IToolRegistry"/> are
/// registered by <c>AIModule</c> - when it is not loaded, tool registration is skipped
/// with a warning (there is no runtime to consume tools anyway).
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
            logger.LogWarning(
                "Skipped AI tool registration for {Assembly}: IToolScanner/IToolRegistry are not registered. " +
                "Ensure AIModule is loaded ([DependsOn(typeof(AIModule))]) if tools should be available.",
                assembly.GetName().Name);
            return 0;
        }

        return ScanAndRegisterAITools(toolRegistry, toolScanner, assembly, logger);
    }

    /// <summary>
    /// Scans <paramref name="assembly"/> for AI tools and registers them into
    /// <paramref name="toolRegistry"/>. Returns the number of tools registered.
    /// Logs a warning on failure (does not throw) so a malformed assembly cannot abort startup.
    /// </summary>
    public static int ScanAndRegisterAITools(IToolRegistry toolRegistry, IToolScanner toolScanner, Assembly assembly, ILogger logger)
    {
        Check.NotNull(toolRegistry);
        Check.NotNull(toolScanner);
        Check.NotNull(assembly);
        Check.NotNull(logger);

        var count = 0;
        try
        {
            foreach (var tool in toolScanner.ScanAssembly(assembly))
            {
                toolRegistry.Register(tool);
                count++;
            }

            if (count > 0)
            {
                logger.LogInformation("Registered {Count} AI tools from {Assembly}", count, assembly.GetName().Name);
            }
            else
            {
                logger.LogDebug("No AI tools found in {Assembly}", assembly.GetName().Name);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to scan and register AI tools from {Assembly}", assembly.GetName().Name);
        }

        return count;
    }
}
