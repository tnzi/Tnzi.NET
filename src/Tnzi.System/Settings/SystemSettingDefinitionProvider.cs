namespace Tnzi.System.Settings;

/// <summary>
/// System 模块内置配置定义 — General/站点信息组，映射 ApplicationOptions（配置节 "System"）。
/// 全部字段经 SettingService.GetApplicationOptions()（IOptionsMonitor）运行时消费。
/// 键体系说明：本组写入 "System:Xxx" 键（IConfiguration 路径，经 SettingConfigurationProvider
/// 热流入 IOptionsMonitor）；SettingService.GetAppNameAsync/GetSiteNameAsync 还有一套旧的
/// "App.AppName"/"App.SiteName" Setting 表直读键 — 旧键无覆盖行时回落到 IOptionsMonitor
/// 当前值，因此两套键并存且配置中心的修改仍可生效；仅当旧键存在覆盖行时旧键优先。
/// </summary>
public class SystemSettingDefinitionProvider : ISettingDefinitionProvider
{
    private const string I18nBase = "admin.modules.system.settings";

    public IReadOnlyList<SettingDefinitionGroup> GetGroups() =>
    [
        new SettingDefinitionGroup
        {
            Key = "system-general",
            ModuleName = "System",
            DisplayName = "General",
            I18nKey = $"{I18nBase}.groups.systemGeneral",
            Icon = "mdi:web",
            Order = 0,
            Fields =
            [
                Field("SiteName", "Site Name", "siteName", () => new ApplicationOptions().SiteName),
                Field("AppName", "App Name", "appName", () => new ApplicationOptions().AppName),
                Field("LogoUrl", "Logo URL", "logoUrl"),
                Field("WebsiteUrl", "Website URL", "websiteUrl"),
                Field("FrontendUrl", "Frontend URL", "frontendUrl"),
                Field("ApiBaseUrl", "API Base URL", "apiBaseUrl"),
                Field("Email", "Email", "email"),
                Field("Phone", "Phone", "phone"),
                Field("CompanyName", "Company", "companyName"),
                Field("Address", "Address", "address"),
                Field("Copyright", "Copyright", "copyright"),
                Field("IcpNumber", "ICP Number", "icpNumber"),
            ],
        },
    ];

    private static SettingFieldDefinition Field(string optionName, string label, string i18nSuffix, Func<string?>? defaultAccessor = null) => new()
    {
        Key = $"System:{optionName}",
        Label = label,
        I18nKey = $"{I18nBase}.fields.{i18nSuffix}",
        DefaultValueAccessor = defaultAccessor,
    };
}
