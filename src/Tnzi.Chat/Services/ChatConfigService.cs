namespace Tnzi.Chat.Services;

public class ChatConfigService : ApplicationService, IChatConfigService
{
    private readonly IOptionsSnapshot<ChatOptions> _options;
    private readonly IChatAccessService _access;

    public ChatConfigService(IServiceProvider serviceProvider, IOptionsSnapshot<ChatOptions> options, IChatAccessService access)
        : base(serviceProvider)
    {
        _options = Check.NotNull(options);
        _access = Check.NotNull(access);
    }

    public async Task<Result<ChatClientConfigDto>> GetClientConfigAsync()
    {
        var o = _options.Value;
        return Ok(new ChatClientConfigDto
        {
            // Per-user access gate: does the caller hold chat.use? Front-end hides the
            // launcher when false. Absence of the Authorization module → fail-open (true).
            Enabled = await _access.CanCurrentUserUseAsync(),
            EnableGroups = o.EnableGroups,
            MaxGroupMembers = o.MaxGroupMembers,
            GroupAvatarMemberCount = Math.Clamp(o.GroupAvatarMemberCount, 1, 9),
            EnablePresence = o.EnablePresence,
            AllowInvisible = o.AllowInvisible,
            EnableMessageSound = o.EnableMessageSound,
            NotificationSound = o.NotificationSound,
            MessageSound = o.MessageSound,
            NewMessageEffect = o.NewMessageEffect,
            FlashTitleOnMessage = o.FlashTitleOnMessage,
            EnableFileMessages = o.EnableFileMessages
        });
    }
}
