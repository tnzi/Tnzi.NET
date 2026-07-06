namespace Tnzi.Chat.Services;

public class ChatConfigService : ApplicationService, IChatConfigService
{
    private readonly IOptionsSnapshot<ChatOptions> _options;

    public ChatConfigService(IServiceProvider serviceProvider, IOptionsSnapshot<ChatOptions> options)
        : base(serviceProvider)
    {
        _options = Check.NotNull(options);
    }

    public Result<ChatClientConfigDto> GetClientConfig()
    {
        var o = _options.Value;
        return Ok(new ChatClientConfigDto
        {
            EnableGroups = o.EnableGroups,
            MaxGroupMembers = o.MaxGroupMembers,
            GroupAvatarMemberCount = Math.Clamp(o.GroupAvatarMemberCount, 1, 9),
            EnablePresence = o.EnablePresence,
            EnableMessageSound = o.EnableMessageSound,
            EnableFileMessages = o.EnableFileMessages
        });
    }
}
