namespace Tnzi.AI.Channels.Adapters.Slack;

/// <summary>
/// Slack adapter configuration. BotToken, AppToken, and SigningSecret
/// are redacted in ToString output to prevent log leakage.
/// </summary>
public class SlackAdapterOptions
{
    /// <summary>Whether the Slack adapter is enabled.</summary>
    public bool Enabled { get; set; }

    /// <summary>Bot token (xoxb- prefix). Inject via env var AI__CHANNELS__SLACK__BOTTOKEN.</summary>
    public string? BotToken { get; set; }

    /// <summary>App-level token (xapp- prefix) for Socket Mode.</summary>
    public string? AppToken { get; set; }

    /// <summary>Signing secret for webhook signature verification.</summary>
    public string? SigningSecret { get; set; }

    /// <summary>
    /// Owning tenant of this channel bot instance. Inbound messages from this
    /// channel are processed under this tenant context (session binding rule
    /// partitioning, thread mapping audit fill). Null = single-tenant / global.
    /// </summary>
    public Guid? TenantId { get; set; }

    /// <summary>Allowed channel ID allowlist (empty = unrestricted).</summary>
    public List<string> AllowedChannels { get; set; } = [];

    /// <summary>Allowed user ID allowlist (empty = unrestricted).</summary>
    public List<string> AllowedUsers { get; set; } = [];

    /// <summary>Maximum length per message (Slack blocks limit is ~4000 chars).</summary>
    public int MaxMessageLength { get; set; } = 4000;

    /// <summary>Retry attempts.</summary>
    public int MaxRetries { get; set; } = 3;

    public override string ToString() =>
        $"SlackAdapterOptions {{ Enabled = {Enabled}, " +
        $"BotToken = {SecretMask.Mask(BotToken)}, " +
        $"AppToken = {SecretMask.Mask(AppToken)}, " +
        $"SigningSecret = {SecretMask.Mask(SigningSecret)}, " +
        $"TenantId = {TenantId?.ToString() ?? "<null>"}, " +
        $"AllowedChannels.Count = {AllowedChannels.Count}, " +
        $"AllowedUsers.Count = {AllowedUsers.Count}, " +
        $"MaxMessageLength = {MaxMessageLength}, MaxRetries = {MaxRetries} }}";
}
