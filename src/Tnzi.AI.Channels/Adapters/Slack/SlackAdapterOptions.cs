namespace Tnzi.AI.Channels.Adapters.Slack;

/// <summary>
/// Slack 适配器配置
/// </summary>
public class SlackAdapterOptions
{
    /// <summary>是否启用 Slack 适配器</summary>
    public bool Enabled { get; set; }

    /// <summary>Bot Token（xoxb-... 格式，建议通过环境变量 AI__CHANNELS__SLACK__BOTTOKEN 注入）</summary>
    public string? BotToken { get; set; }

    /// <summary>App-Level Token（xapp-... 格式，Socket Mode 使用）</summary>
    public string? AppToken { get; set; }

    /// <summary>Signing Secret（Webhook 签名验证）</summary>
    public string? SigningSecret { get; set; }

    /// <summary>允许的 Channel ID 白名单（空=不限制）</summary>
    public List<string> AllowedChannels { get; set; } = [];

    /// <summary>允许的用户 ID 白名单（空=不限制）</summary>
    public List<string> AllowedUsers { get; set; } = [];

    /// <summary>单条消息最大长度（Slack blocks 限制约 4000 字符）</summary>
    public int MaxMessageLength { get; set; } = 4000;

    /// <summary>重试次数</summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>文件上传最大大小（字节）</summary>
    public long MaxFileSize { get; set; } = 50 * 1024 * 1024;
}
