using Tnzi.AI.Channels.Options;

namespace Tnzi.AI.Channels.Adapters.Dingtalk;

/// <summary>
/// DingTalk adapter configuration. AppKey and AppSecret are redacted
/// in ToString output to prevent log leakage.
/// </summary>
public class DingtalkAdapterOptions
{
    /// <summary>Whether the DingTalk adapter is enabled.</summary>
    public bool Enabled { get; set; }

    /// <summary>Application AppKey. Inject via env var AI__CHANNELS__DINGTALK__APPKEY.</summary>
    public string? AppKey { get; set; }

    /// <summary>
    /// Application AppSecret. Also used for HMAC-SHA256 signature verification of
    /// incoming webhooks; when set, the no-headers
    /// <see cref="DingtalkChannelAdapter.HandleEventAsync(string, CancellationToken)"/>
    /// overload rejects events because signature verification cannot be performed.
    /// </summary>
    public string? AppSecret { get; set; }

    /// <summary>Robot code (used for receive-side routing).</summary>
    public string? RobotCode { get; set; }

    /// <summary>Allowed user ID allowlist (empty = unrestricted).</summary>
    public List<string> AllowedUsers { get; set; } = [];

    /// <summary>Allowed organization ID allowlist (empty = unrestricted).</summary>
    public List<string> AllowedOrganizations { get; set; } = [];

    /// <summary>Maximum length per message (DingTalk markdown limit is ~20000 chars).</summary>
    public int MaxMessageLength { get; set; } = 20000;

    /// <summary>Retry attempts.</summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Whether to verify webhook signatures on incoming events. Default: true.
    /// When true, the no-headers
    /// <see cref="DingtalkChannelAdapter.HandleEventAsync(string, CancellationToken)"/>
    /// overload rejects events because signature verification cannot be performed
    /// without the timestamp/sign headers. Set to false only for trusted private
    /// networks where webhook signatures are unavailable (e.g. a fronting gateway
    /// already validated the request).
    /// </summary>
    public bool VerifyWebhookSignature { get; set; } = true;

    public override string ToString() =>
        $"DingtalkAdapterOptions {{ Enabled = {Enabled}, " +
        $"AppKey = {SecretMask.Mask(AppKey)}, " +
        $"AppSecret = {SecretMask.Mask(AppSecret)}, " +
        $"RobotCode = {RobotCode ?? "<null>"}, " +
        $"AllowedUsers.Count = {AllowedUsers.Count}, " +
        $"AllowedOrganizations.Count = {AllowedOrganizations.Count}, " +
        $"MaxMessageLength = {MaxMessageLength}, MaxRetries = {MaxRetries}, " +
        $"VerifyWebhookSignature = {VerifyWebhookSignature} }}";
}
