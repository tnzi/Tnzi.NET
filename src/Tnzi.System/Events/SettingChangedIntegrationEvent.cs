namespace Tnzi.System.Events;

/// <summary>
/// 配置变更集成事件（跨实例）。分布式总线可用时随本地 <see cref="SettingChangedEvent"/> 一起发布，
/// 让多实例部署下其他实例也能 reload IConfiguration + 广播 SignalR。
/// 刻意不携带值：收端直查数据库 reload，配置值（尤其机密）不落 broker。
/// </summary>
/// <remarks>
/// ★ 它是 <see cref="IBroadcastIntegrationEvent"/>：每个实例的 MemoryCache / IConfiguration / SignalR 连接
/// 都是各自独立的，这件事必须在<b>每个进程</b>里各做一次。按普通集成事件订阅时同一服务的 N 个实例
/// 共用一条工作队列，代理只投给其中一个 —— 1/N 概率回到发布实例（处理器按 OriginInstanceId 直接 return，
/// 等于哪都没应用），其余实例继续用旧值直到重启，而界面与日志全都显示成功。
/// 发布实例自己也会收到一份回环，用 <see cref="OriginInstanceId"/> 比对 <see cref="TnziInstance.Id"/> 跳过。
/// </remarks>
public class SettingChangedIntegrationEvent : EventBase, IBroadcastIntegrationEvent
{
    public string SourceService => "Tnzi.System";

    public required string Key { get; init; }

    public required SettingScope Scope { get; init; }

    public string? ScopeId { get; init; }

    public bool IsRemoval { get; init; }

    /// <summary>发布方进程实例标识。收端据此跳过回环投递（发布实例的本地链已处理）。</summary>
    public required Guid OriginInstanceId { get; init; }
}
