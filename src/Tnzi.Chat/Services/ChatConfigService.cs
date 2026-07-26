namespace Tnzi.Chat.Services;

public class ChatConfigService : ApplicationService, IChatConfigService
{
    private readonly IOptionsSnapshot<ChatOptions> _options;
    private readonly IOptionsSnapshot<PresenceOptions> _presenceOptions;
    private readonly IChatAccessService _access;

    public ChatConfigService(
        IServiceProvider serviceProvider,
        IOptionsSnapshot<ChatOptions> options,
        IOptionsSnapshot<PresenceOptions> presenceOptions,
        IChatAccessService access)
        : base(serviceProvider)
    {
        _options = Check.NotNull(options);
        _presenceOptions = Check.NotNull(presenceOptions);
        _access = Check.NotNull(access);
    }

    public async Task<Result<ChatClientConfigDto>> GetClientConfigAsync()
    {
        var o = _options.Value;
        // Presence 开关现由 Presence 模块拥有；chat 客户端配置继续暴露这两个字段（前端不变），改从 PresenceOptions 读。
        var p = _presenceOptions.Value;
        return Ok(new ChatClientConfigDto
        {
            // Per-user access gate: does the caller hold chat.use? Front-end hides the
            // launcher when false. Absence of the Authorization module → fail-open (true).
            Enabled = await _access.CanCurrentUserUseAsync(),
            EnableGroups = o.EnableGroups,
            MaxGroupMembers = o.MaxGroupMembers,
            GroupAvatarMemberCount = Math.Clamp(o.GroupAvatarMemberCount, 1, 9),
            EnablePresence = p.EnablePresence,
            AllowInvisible = p.AllowInvisible,
            EnableMessageSound = o.EnableMessageSound,
            NotificationSound = o.NotificationSound,
            MessageSound = o.MessageSound,
            NewMessageEffect = o.NewMessageEffect,
            FlashTitleOnMessage = o.FlashTitleOnMessage,
            EnableFileMessages = o.EnableFileMessages
        });
    }
}
