namespace Tnzi.AI.Metadata;

/// <summary>
/// Provider 来源常量 — <see cref="ProviderDto.Source"/> 的取值
/// </summary>
public static class ProviderSources
{
    /// <summary>数据库实体来源（admin 录入，可编辑/删除）</summary>
    public const string Database = "Database";

    /// <summary>appsettings 配置来源（AI:Providers 节，只读）</summary>
    public const string Configuration = "Configuration";
}
