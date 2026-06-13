namespace Tnzi.AI.Channels.Adapters.Telegram;

/// <summary>
/// Telegram adapter configuration. BotToken is redacted in ToString output
/// to prevent log leakage.
/// </summary>
public class TelegramAdapterOptions
{
    /// <summary>Whether the Telegram adapter is enabled.</summary>
    public bool Enabled { get; set; }

    /// <summary>Bot token. Inject via env var AI__CHANNELS__TELEGRAM__BOTTOKEN.</summary>
    public string? BotToken { get; set; }

    /// <summary>
    /// Owning tenant of this channel bot instance. Inbound messages from this
    /// channel are processed under this tenant context (session binding rule
    /// partitioning, thread mapping audit fill). Null = single-tenant / global.
    /// </summary>
    public Guid? TenantId { get; set; }

    /// <summary>Allowed user ID allowlist (empty = unrestricted).</summary>
    public List<long> AllowedUsers { get; set; } = [];

    /// <summary>Long-polling timeout in seconds.</summary>
    public int PollingTimeoutSeconds { get; set; } = 30;

    /// <summary>Retry attempts.</summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>Maximum photo size in bytes.</summary>
    public long MaxPhotoSize { get; set; } = 10 * 1024 * 1024;

    /// <summary>Maximum document size in bytes.</summary>
    public long MaxDocumentSize { get; set; } = 50 * 1024 * 1024;

    public override string ToString() =>
        $"TelegramAdapterOptions {{ Enabled = {Enabled}, " +
        $"BotToken = {SecretMask.Mask(BotToken)}, " +
        $"TenantId = {TenantId?.ToString() ?? "<null>"}, " +
        $"AllowedUsers.Count = {AllowedUsers.Count}, " +
        $"PollingTimeoutSeconds = {PollingTimeoutSeconds}, MaxRetries = {MaxRetries}, " +
        $"MaxPhotoSize = {MaxPhotoSize}, MaxDocumentSize = {MaxDocumentSize} }}";
}
