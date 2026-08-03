namespace Tnzi.SignalR.Extensions;

/// <summary>
/// Hub mapping helpers that keep the admin shell's realtime signal honest.
/// </summary>
public static class HubEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Map a hub AND record it in <see cref="IRealtimeHubRegistry"/>, so
    /// <c>GET admin/shell/modules</c> can tell the client this hub exists and
    /// where to reach it.
    ///
    /// Use this instead of the raw <c>MapHub&lt;THub&gt;</c> for any framework
    /// hub a client is expected to discover. Modules map their hubs only when
    /// <c>SignalRModule</c> is loaded, so an unrecorded hub is exactly the case
    /// where a client must NOT connect - the frontend previously had no way to
    /// learn that and retried a 404 forever.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder (the web application).</param>
    /// <param name="name">
    /// Stable logical name the client keys off, e.g. <c>"settings"</c>. Keep it
    /// independent of the route so the path can move without breaking clients.
    /// </param>
    /// <param name="pattern">Route pattern, e.g. <c>"/hubs/settings"</c>.</param>
    public static HubEndpointConventionBuilder MapTnziHub<THub>(
        this IEndpointRouteBuilder endpoints,
        string name,
        string pattern)
        where THub : Hub
    {
        Check.NotNull(endpoints);
        Check.NotNullOrWhiteSpace(name);
        Check.NotNullOrWhiteSpace(pattern);

        var builder = endpoints.MapHub<THub>(pattern);
        // Optional by design: a host can compose the SignalR module without the
        // AspNetCore admin shell. Mapping must not fail because nobody is
        // listening for the announcement.
        endpoints.ServiceProvider.GetService<IRealtimeHubRegistry>()?.Register(name, pattern);
        return builder;
    }
}
