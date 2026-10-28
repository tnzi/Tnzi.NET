namespace Tnzi.AI.Channels.Adapters.Feishu;

/// <summary>
/// Feishu (Lark) adapter configuration. AppSecret, VerificationToken,
/// and EncryptKey are redacted in ToString output to prevent log leakage.
/// </summary>
public class FeishuAdapterOptions
{
    /// <summary>Whether the Feishu adapter is enabled.</summary>
    public bool Enabled { get; set; }

    /// <summary>Feishu application App ID.</summary>
    public string AppId { get; set; } = string.Empty;

    /// <summary>Feishu application App Secret.</summary>
    public string AppSecret { get; set; } = string.Empty;

    /// <summary>Event verification token (optional, used in webhook mode).</summary>
    public string? VerificationToken { get; set; }

    /// <summary>
    /// Encrypt key (required for HMAC signature verification of incoming webhooks;
    /// when set, <see cref="FeishuChannelAdapter.HandleEventAsync(string, IDictionary{string, string}?, CancellationToken)"/>
    /// requires headers and rejects events with invalid or missing signatures).
    /// </summary>
    public string? EncryptKey { get; set; }

    /// <summary>Allowed sender open_id list (empty = unrestricted).</summary>
    public List<string> AllowedUserIds { get; set; } = [];

    /// <summary>
    /// Owning tenant of this channel bot instance. Inbound messages from this
    /// channel are processed under this tenant context (session binding rule
    /// partitioning, thread mapping audit fill). Null = single-tenant / global.
    /// </summary>
    public Guid? TenantId { get; set; }

    /// <summary>
    /// Maximum characters per message; longer replies are sent as several messages.
    /// Feishu caps a text message request at 150 KB and the text is JSON-encoded twice
    /// (non-ASCII characters can take up to 7 bytes each), so the default stays well below that.
    /// </summary>
    public int MaxMessageLength { get; set; } = 10000;

    /// <summary>Retry attempts per message chunk.</summary>
    public int MaxRetries { get; set; } = 3;

    public override string ToString() =>
        $"FeishuAdapterOptions {{ Enabled = {Enabled}, AppId = {AppId}, " +
        $"AppSecret = {SecretMask.Mask(AppSecret)}, " +
        $"VerificationToken = {SecretMask.Mask(VerificationToken)}, " +
        $"EncryptKey = {SecretMask.Mask(EncryptKey)}, " +
        $"TenantId = {TenantId?.ToString() ?? "<null>"}, " +
        $"AllowedUserIds.Count = {AllowedUserIds.Count}, " +
        $"MaxMessageLength = {MaxMessageLength}, MaxRetries = {MaxRetries} }}";
}
