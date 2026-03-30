namespace Tnzi.AI.Channels.Adapters.Feishu;

/// <summary>
/// 飞书适配器配置
/// </summary>
public class FeishuAdapterOptions
{
    /// <summary>是否启用飞书适配器</summary>
    public bool Enabled { get; set; }

    /// <summary>飞书应用 App ID</summary>
    public string AppId { get; set; } = string.Empty;

    /// <summary>飞书应用 App Secret</summary>
    public string AppSecret { get; set; } = string.Empty;

    /// <summary>事件验证 Token（可选，Webhook 模式使用）</summary>
    public string? VerificationToken { get; set; }

    /// <summary>Encrypt Key（可选，消息加密）</summary>
    public string? EncryptKey { get; set; }

    /// <summary>允许的用户 Open ID 列表（为空允许所有）</summary>
    public List<string> AllowedUserIds { get; set; } = [];
}
