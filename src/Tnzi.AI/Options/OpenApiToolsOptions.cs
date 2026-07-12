namespace Tnzi.AI.Options;

/// <summary>
/// OpenAPI 工具自动生成配置选项
/// </summary>
/// <remarks>
/// 从 OpenAPI 3.x 规范自动生成 AITool，使 AI Agent 能够调用外部 REST API。
/// 配置路径：AI:OpenApiTools
/// </remarks>
[ConfigSection("AI:OpenApiTools")]
[RuntimeSettingGroup(Key = "ai-tools", Module = "AI", DisplayName = "Tools",
    I18nKey = "admin.modules.system.settings.groups.aiTools", Icon = "mdi:tools", Order = 130)]
[ExperimentalApi(Reason = "OpenAPI tool generation is evolving")]
public class OpenApiToolsOptions
{
    /// <summary>
    /// 是否启用 OpenAPI 工具生成（默认关闭）
    /// </summary>
    [RuntimeSetting(Label = "OpenAPI Tools Enabled", I18n = "admin.modules.system.settings.fields.openApiToolsEnabled",
        Type = SettingFieldType.Boolean)]
    public bool Enabled { get; set; }

    /// <summary>
    /// OpenAPI 规范列表
    /// </summary>
    public List<OpenApiSpec> Specs { get; set; } = [];
}

/// <summary>
/// OpenAPI 规范配置
/// </summary>
[ExperimentalApi(Reason = "OpenAPI tool generation is evolving")]
public class OpenApiSpec
{
    /// <summary>
    /// OpenAPI 规范文件的 URL（支持 JSON 和 YAML）
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// 规范名称（用于工具分组和日志标识）
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 自定义 HTTP 请求头（如鉴权 Bearer Token）
    /// </summary>
    public Dictionary<string, string> Headers { get; set; } = [];

    /// <summary>
    /// 包含的操作 ID 列表（为空则包含所有操作）
    /// </summary>
    /// <remarks>
    /// 与 ExcludeOperations 互斥，优先使用 IncludeOperations
    /// </remarks>
    public List<string> IncludeOperations { get; set; } = [];

    /// <summary>
    /// 排除的操作 ID 列表
    /// </summary>
    public List<string> ExcludeOperations { get; set; } = [];
}
