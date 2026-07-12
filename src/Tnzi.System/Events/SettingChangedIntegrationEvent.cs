namespace Tnzi.System.Events;

/// <summary>
/// 配置变更集成事件（跨实例）。分布式总线可用时随本地 <see cref="SettingChangedEvent"/> 一起发布，
/// 让多实例部署下其他实例也能 reload IConfiguration + 广播 SignalR。
/// 刻意不携带值：收端直查数据库 reload，配置值（尤其机密）不落 broker。
/// </summary>
public class SettingChangedIntegrationEvent : EventBase, IIntegrationEvent
{
    /// <summary>
    /// 本进程实例标识。发布方填入 <see cref="OriginInstanceId"/>，收端据此跳过
    /// broker 回投给发布实例自身的事件（本地链已 reload + 广播过）。
    /// </summary>
    public static Guid LocalInstanceId { get; } = Guid.NewGuid();

    public string SourceService => "Tnzi.System";

    public required string Key { get; init; }

    public required SettingScope Scope { get; init; }

    public string? ScopeId { get; init; }

    public bool IsRemoval { get; init; }

    /// <summary>发布方进程实例标识。收端据此跳过回环投递（发布实例的本地链已处理）。</summary>
    public required Guid OriginInstanceId { get; init; }
}
