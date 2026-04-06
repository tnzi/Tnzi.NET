namespace Tnzi.AI.Channels.Adapters.Discord;

/// <summary>
/// Discord 适配器配置
/// </summary>
public class DiscordAdapterOptions
{
    /// <summary>是否启用 Discord 适配器</summary>
    public bool Enabled { get; set; }

    /// <summary>Bot Token（建议通过环境变量 AI__CHANNELS__DISCORD__BOTTOKEN 注入）</summary>
    public string? BotToken { get; set; }

    /// <summary>Application ID</summary>
    public string? ApplicationId { get; set; }

    /// <summary>Application Public Key（用于 Webhook 签名验证，Ed25519）</summary>
    public string? PublicKey { get; set; }

    /// <summary>允许的 Guild（服务器）ID 白名单（空=不限制）</summary>
    public List<string> AllowedGuilds { get; set; } = [];

    /// <summary>允许的 Channel ID 白名单（空=不限制）</summary>
    public List<string> AllowedChannels { get; set; } = [];

    /// <summary>允许的用户 ID 白名单（空=不限制）</summary>
    public List<string> AllowedUsers { get; set; } = [];

    /// <summary>单条消息最大长度（Discord 限制 2000 字符）</summary>
    public int MaxMessageLength { get; set; } = 2000;

    /// <summary>重试次数</summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>文件上传最大大小（字节，Discord 免费限制 25MB）</summary>
    public long MaxFileSize { get; set; } = 25 * 1024 * 1024;
}
