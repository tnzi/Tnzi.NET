namespace Tnzi.AI.Channels.Adapters.Dingtalk;

/// <summary>
/// 钉钉适配器配置
/// </summary>
public class DingtalkAdapterOptions
{
    /// <summary>是否启用钉钉适配器</summary>
    public bool Enabled { get; set; }

    /// <summary>应用 AppKey（建议通过环境变量 AI__CHANNELS__DINGTALK__APPKEY 注入）</summary>
    public string? AppKey { get; set; }

    /// <summary>应用 AppSecret（建议通过环境变量 AI__CHANNELS__DINGTALK__APPSECRET 注入）</summary>
    public string? AppSecret { get; set; }

    /// <summary>机器人编码（用于接收消息路由）</summary>
    public string? RobotCode { get; set; }

    /// <summary>允许的用户 ID 白名单（空=不限制）</summary>
    public List<string> AllowedUsers { get; set; } = [];

    /// <summary>允许的组织 ID 白名单（空=不限制）</summary>
    public List<string> AllowedOrganizations { get; set; } = [];

    /// <summary>单条消息最大长度（钉钉 Markdown 限制约 20000 字符）</summary>
    public int MaxMessageLength { get; set; } = 20000;

    /// <summary>重试次数</summary>
    public int MaxRetries { get; set; } = 3;
}
