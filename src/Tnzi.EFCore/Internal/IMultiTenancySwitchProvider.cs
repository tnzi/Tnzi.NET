namespace Tnzi.EFCore.Internal;

/// <summary>
/// 为 EF 模型缓存键提供多租户开关状态
/// </summary>
public interface IMultiTenancySwitchProvider
{
    bool IsMultiTenancyEnabled { get; }
}
