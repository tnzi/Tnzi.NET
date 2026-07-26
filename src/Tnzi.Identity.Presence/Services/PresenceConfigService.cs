namespace Tnzi.Identity.Presence.Services;

public class PresenceConfigService : ApplicationService, IPresenceConfigService
{
    private readonly IOptionsSnapshot<PresenceOptions> _options;

    public PresenceConfigService(IServiceProvider serviceProvider, IOptionsSnapshot<PresenceOptions> options)
        : base(serviceProvider)
    {
        _options = Check.NotNull(options);
    }

    public Task<Result<PresenceClientConfigDto>> GetClientConfigAsync()
    {
        var o = _options.Value;
        return Task.FromResult(Ok(new PresenceClientConfigDto
        {
            EnablePresence = o.EnablePresence,
            AllowInvisible = o.AllowInvisible,
            AutoAwayEnabled = o.AutoAwayEnabled,
            AutoAwayMinutes = o.AutoAwayMinutes
        }));
    }
}
