namespace Tnzi.Settings;

/// <summary>
/// 模块配置定义提供者。模块在 ConfigureServicesAsync 中注册：
/// <code>context.Services.AddSingleton&lt;ISettingDefinitionProvider, XxxSettingDefinitionProvider&gt;();</code>
/// System 模块未加载时定义惰性无害（无聚合端点）。
/// </summary>
[ExperimentalApi(Reason = "Settings center contract is new and may evolve")]
public interface ISettingDefinitionProvider
{
    IReadOnlyList<SettingDefinitionGroup> GetGroups();
}
