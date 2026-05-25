namespace Tnzi.System.Events;

/// <summary>
/// 配置变更事件。Set/Update/Delete 时由 SettingService 发布，
/// 由 SettingConfigurationProvider 订阅以触发 IOptionsMonitor 重新加载。
/// </summary>
public class SettingChangedEvent : EventBase
{
    public required string Key { get; init; }

    public required SettingScope Scope { get; init; }

    public string? ScopeId { get; init; }

    /// <summary>
    /// 变更后的明文值。删除场景为 null 且 IsRemoval=true。
    /// 加密配置发布的是密文（与 Setting.Value 字段一致），订阅方按需解密。
    /// </summary>
    public string? NewValue { get; init; }

    public bool IsRemoval { get; init; }
}
