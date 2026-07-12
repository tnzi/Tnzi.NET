namespace Tnzi.AI.Channels.Options;

/// <summary>
/// IM Channel Bridge 模块配置
/// </summary>
public class ChannelsModuleOptions
{
    /// <summary>是否启用 IM Channel Bridge</summary>
    public bool Enabled { get; set; }

    /// <summary>线程存储方式：Database / File</summary>
    public string ThreadStore { get; set; } = "Database";

    /// <summary>文件存储路径（ThreadStore=File 时使用）</summary>
    public string FileStorePath { get; set; } = ".tnzi-ai/channels/threads.json";

    /// <summary>最大并发消息处理数</summary>
    public int MaxConcurrency { get; set; } = 5;

    /// <summary>默认 Agent ID（为空则使用系统默认 Agent）</summary>
    public Guid? DefaultAgentId { get; set; }

    /// <summary>各平台适配器配置</summary>
    public TelegramAdapterOptions Telegram { get; set; } = new();

    /// <summary>飞书配置</summary>
    public FeishuAdapterOptions Feishu { get; set; } = new();

    /// <summary>Slack 配置</summary>
    public SlackAdapterOptions Slack { get; set; } = new();

    /// <summary>Discord 配置</summary>
    public DiscordAdapterOptions Discord { get; set; } = new();

    /// <summary>钉钉配置</summary>
    public DingtalkAdapterOptions Dingtalk { get; set; } = new();

    /// <summary>Gateway 配置</summary>
    public GatewayOptions Gateway { get; set; } = new();
}
