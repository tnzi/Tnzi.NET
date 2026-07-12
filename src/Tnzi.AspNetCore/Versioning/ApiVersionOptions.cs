namespace Tnzi.AspNetCore.Versioning;

/// <summary>
/// API 版本控制选项
/// </summary>
[ConfigSection("AspNetCore:ApiVersion")]
[RuntimeSettingGroup(Key = "web-apiversion", Module = "Web", DisplayName = "API Versioning",
    I18nKey = "admin.modules.system.settings.groups.webApiVersion",
    Icon = "mdi:tag-multiple-outline", Order = 705, PermissionGroup = "system")]
public class ApiVersionOptions
{
    /// <summary>
    /// 是否启用 API 版本控制
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// 默认 API 版本（未指定时使用）
    /// </summary>
    public string DefaultVersion { get; set; } = "1.0";

    /// <summary>
    /// 版本读取方式
    /// </summary>
    public ApiVersionReaderType ReaderType { get; set; } = ApiVersionReaderType.QueryString;

    /// <summary>
    /// Header 方式下的 Header 名称
    /// </summary>
    public string HeaderName { get; set; } = "X-Api-Version";

    /// <summary>
    /// QueryString 方式下的参数名称
    /// </summary>
    public string QueryStringName { get; set; } = "api-version";

    /// <summary>
    /// URL 方式下的版本参数名称
    /// </summary>
    public string UrlParameterName { get; set; } = "v";

    /// <summary>
    /// 是否在响应头中返回 API 版本
    /// </summary>
    [RuntimeSetting(Label = "Report Version In Response Header", I18n = "admin.modules.system.settings.fields.apiVersionReportVersion",
        Type = SettingFieldType.Boolean,
        Description = "When enabled, the resolved API version is echoed back in the response header. Only takes effect when API versioning is enabled.")]
    public bool ReportVersion { get; set; } = true;

    /// <summary>
    /// 响应头中返回版本的 Header 名称
    /// </summary>
    public string VersionHeaderName { get; set; } = "X-Api-Version";
}

/// <summary>
/// API 版本读取方式
/// </summary>
public enum ApiVersionReaderType
{
    /// <summary>
    /// 从 QueryString 读取（默认）
    /// </summary>
    QueryString,

    /// <summary>
    /// 从 Header 读取
    /// </summary>
    Header,

    /// <summary>
    /// 从 URL 路径读取
    /// </summary>
    UrlPath
}
