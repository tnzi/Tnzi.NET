using Tnzi.AI.Channels.Options;

namespace Tnzi.AI.Channels.Adapters.Discord;

/// <summary>
/// Discord adapter configuration. BotToken is redacted and PublicKey
/// is partially hidden in ToString output to prevent log leakage.
/// </summary>
public class DiscordAdapterOptions
{
    /// <summary>Whether the Discord adapter is enabled.</summary>
    public bool Enabled { get; set; }

    /// <summary>Bot token. Inject via env var AI__CHANNELS__DISCORD__BOTTOKEN.</summary>
    public string? BotToken { get; set; }

    /// <summary>Application ID.</summary>
    public string? ApplicationId { get; set; }

    /// <summary>Application public key for webhook signature verification (Ed25519).</summary>
    public string? PublicKey { get; set; }

    /// <summary>Allowed guild ID allowlist (empty = unrestricted).</summary>
    public List<string> AllowedGuilds { get; set; } = [];

    /// <summary>Allowed channel ID allowlist (empty = unrestricted).</summary>
    public List<string> AllowedChannels { get; set; } = [];

    /// <summary>Allowed user ID allowlist (empty = unrestricted).</summary>
    public List<string> AllowedUsers { get; set; } = [];

    /// <summary>Maximum length per message (Discord limit is 2000 chars).</summary>
    public int MaxMessageLength { get; set; } = 2000;

    /// <summary>Retry attempts.</summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>Maximum file upload size in bytes (Discord free tier is 25MB).</summary>
    public long MaxFileSize { get; set; } = 25 * 1024 * 1024;

    public override string ToString() =>
        $"DiscordAdapterOptions {{ Enabled = {Enabled}, " +
        $"BotToken = {SecretMask.Mask(BotToken)}, " +
        $"ApplicationId = {ApplicationId ?? "<null>"}, " +
        $"PublicKey = {SecretMask.Mask(PublicKey)}, " +
        $"AllowedGuilds.Count = {AllowedGuilds.Count}, " +
        $"AllowedChannels.Count = {AllowedChannels.Count}, " +
        $"AllowedUsers.Count = {AllowedUsers.Count}, " +
        $"MaxMessageLength = {MaxMessageLength}, MaxRetries = {MaxRetries}, MaxFileSize = {MaxFileSize} }}";
}
