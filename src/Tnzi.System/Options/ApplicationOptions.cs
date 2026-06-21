namespace Tnzi.System.Options;

/// <summary>应用程序配置选项。配置路径：System（经配置中心可热设置）。</summary>
[ConfigSection("System")]
[RuntimeSettingGroup(Key = "system-general", Module = "System", DisplayName = "General",
    Icon = "mdi:web", Order = 0, I18nKey = "admin.modules.system.settings.groups.systemGeneral")]
public class ApplicationOptions
{
    [RuntimeSetting(Label = "App Name", I18n = "admin.modules.system.settings.fields.appName")]
    public string AppName { get; set; } = "Tnzi.NET";

    [RuntimeSetting(Label = "Site Name", I18n = "admin.modules.system.settings.fields.siteName")]
    public string SiteName { get; set; } = "Tnzi.NET";

    [RuntimeSetting(Label = "Frontend URL", I18n = "admin.modules.system.settings.fields.frontendUrl")]
    public string? FrontendUrl { get; set; }

    [RuntimeSetting(Label = "API Base URL", I18n = "admin.modules.system.settings.fields.apiBaseUrl")]
    public string? ApiBaseUrl { get; set; }

    [RuntimeSetting(Label = "Email", I18n = "admin.modules.system.settings.fields.email")]
    public string? Email { get; set; }

    [RuntimeSetting(Label = "Phone", I18n = "admin.modules.system.settings.fields.phone")]
    public string? Phone { get; set; }

    [RuntimeSetting(Label = "Company", I18n = "admin.modules.system.settings.fields.companyName")]
    public string? CompanyName { get; set; }

    [RuntimeSetting(Label = "Address", I18n = "admin.modules.system.settings.fields.address")]
    public string? Address { get; set; }

    [RuntimeSetting(Label = "Website URL", I18n = "admin.modules.system.settings.fields.websiteUrl")]
    public string? WebsiteUrl { get; set; }

    [RuntimeSetting(Label = "Logo URL", I18n = "admin.modules.system.settings.fields.logoUrl")]
    public string? LogoUrl { get; set; }

    [RuntimeSetting(Label = "Copyright", I18n = "admin.modules.system.settings.fields.copyright")]
    public string? Copyright { get; set; }

    [RuntimeSetting(Label = "ICP Number", I18n = "admin.modules.system.settings.fields.icpNumber")]
    public string? IcpNumber { get; set; }
}
