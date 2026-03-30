namespace Tnzi.AI.Channels.Options;

/// <summary>
/// Telegram 适配器配置
/// </summary>
public class TelegramAdapterOptions
{
    /// <summary>是否启用 Telegram 适配器</summary>
    public bool Enabled { get; set; }

    /// <summary>Bot Token（建议通过环境变量 AI__CHANNELS__TELEGRAM__BOTTOKEN 注入）</summary>
    public string? BotToken { get; set; }

    /// <summary>允许使用 Bot 的用户 ID 白名单（空=不限制）</summary>
    public List<long> AllowedUsers { get; set; } = [];

    /// <summary>长轮询超时（秒）</summary>
    public int PollingTimeoutSeconds { get; set; } = 30;

    /// <summary>重试次数</summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>照片最大大小（字节）</summary>
    public long MaxPhotoSize { get; set; } = 10 * 1024 * 1024;

    /// <summary>文档最大大小（字节）</summary>
    public long MaxDocumentSize { get; set; } = 50 * 1024 * 1024;
}
