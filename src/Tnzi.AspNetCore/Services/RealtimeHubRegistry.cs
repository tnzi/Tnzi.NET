namespace Tnzi.AspNetCore.Services;

/// <summary>
/// One realtime hub the host actually mapped, as reported to the admin shell.
/// </summary>
/// <param name="Name">
/// Stable logical name the client keys off, e.g. <c>"settings"</c>, <c>"chat"</c>,
/// <c>"presence"</c>. Independent of the route, so a deployment may relocate the
/// endpoint without breaking the client.
/// </param>
/// <param name="Path">
/// Application-relative path the hub is mapped at, e.g. <c>"/hubs/settings"</c>
/// (without <c>PathBase</c> - the shell endpoint prefixes it per request).
/// </param>
public sealed record RealtimeHubDescriptor(string Name, string Path);

/// <summary>
/// Registry of the SignalR hubs the host actually mapped during startup.
///
/// Exists because the admin frontend cannot infer realtime availability from
/// anything else it is told: the loaded-module signal
/// (<c>GET admin/shell/modules</c>) only reports <c>TnziApplicationModule</c>
/// business modules by design, and <c>SignalRModule</c> is a
/// <c>TnziFrameworkModule</c> - so "SignalR missing from the module list" is
/// true whether or not SignalR is loaded. Worse, "SignalR loaded" is not the
/// question the client actually has: hubs are mapped by their OWNING modules
/// (System maps <c>/hubs/settings</c>, Chat maps <c>/hubs/chat</c>, ...), each
/// guarded on SignalR being present. Only the mapping call site knows the truth,
/// so that is where it gets recorded.
///
/// Without this, a host that loaded System but not SignalR still passed the
/// frontend's gate, opened a connection to a hub that was never mapped, and
/// retried against a 404 forever - while live settings hot-reload silently did
/// not work and nobody was told the channel was dead.
///
/// Populated by <c>MapTnziHub&lt;THub&gt;()</c> (Tnzi.SignalR); read by
/// <c>DefaultAdminShellController</c>. A host without SignalR registers nothing,
/// and the shell reports realtime as unavailable.
/// </summary>
[StableApi(Since = "0.1.0")]
public interface IRealtimeHubRegistry
{
    /// <summary>The hubs mapped so far, in registration order.</summary>
    IReadOnlyList<RealtimeHubDescriptor> Hubs { get; }

    /// <summary>
    /// Record that <paramref name="name"/> is served at <paramref name="path"/>.
    /// Idempotent per name - re-registering the same name replaces the path
    /// rather than emitting a duplicate, so a module whose initialization runs
    /// twice (tests, host restarts inside one process) cannot double-report.
    /// </summary>
    void Register(string name, string path);
}

/// <summary>
/// Default in-memory <see cref="IRealtimeHubRegistry"/>. Registered as a
/// singleton by <c>AspNetCoreModule</c>; written only during application
/// initialization and read per request, but locked anyway so a host that maps
/// hubs from several modules concurrently cannot tear the list.
/// </summary>
public class RealtimeHubRegistry : IRealtimeHubRegistry
{
    private readonly Lock _lock = new();
    private readonly List<RealtimeHubDescriptor> _hubs = [];

    /// <inheritdoc />
    public IReadOnlyList<RealtimeHubDescriptor> Hubs
    {
        get
        {
            lock (_lock)
            {
                return _hubs.ToArray();
            }
        }
    }

    /// <inheritdoc />
    public void Register(string name, string path)
    {
        Check.NotNullOrWhiteSpace(name);
        Check.NotNullOrWhiteSpace(path);

        lock (_lock)
        {
            var index = _hubs.FindIndex(h => string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase));
            var descriptor = new RealtimeHubDescriptor(name, path);
            if (index >= 0)
            {
                _hubs[index] = descriptor;
            }
            else
            {
                _hubs.Add(descriptor);
            }
        }
    }
}
