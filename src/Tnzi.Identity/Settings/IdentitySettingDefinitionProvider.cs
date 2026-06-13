namespace Tnzi.Identity.Settings;

/// <summary>
/// Identity 模块内置配置定义 — 仅收录经 IOptionsMonitor.CurrentValue 运行时热消费的字段。
/// 消费者映射：
///   identity-security  → AuthService（EnableLockout：CheckPasswordSignInAsync 的 lockoutOnFailure 实参每次热读）
///   identity-registration → AuthService（SignIn 开关 + Registration 开关）
///                           RegistrationService（EnableQuickRegisterEmail/EnableQuickRegisterSms/RequireConfirmedEmail）
///                           Hosting UserRegisteredEventHandler（RequireConfirmedEmail，已切 IOptionsMonitor）
/// 不收录（双轨冻结）：PasswordPolicy 全部字段与 MaxFailedLoginAttempts/LockoutDurationMinutes —
///   它们在 AddTnziIdentity 启动期被写入 Microsoft AddIdentity(options) lambda，UserManager 内置
///   验证器/Lockout 机制按单例快照执行；若收录会出现「自定义服务按新值、UserManager 按旧值」的
///   双轨分裂（收紧密码策略时形成安全缺口）。待 Microsoft IdentityOptions 支持热更新后再回填。
/// 安全红线：JWT SecretKey、OAuth ClientId/ClientSecret 等凭证字段不得收录。
/// </summary>
public class IdentitySettingDefinitionProvider : ISettingDefinitionProvider
{
    private const string I18nBase = "admin.modules.system.settings";

    public IReadOnlyList<SettingDefinitionGroup> GetGroups() =>
    [
        new SettingDefinitionGroup
        {
            Key = "identity-security",
            ModuleName = "Identity",
            DisplayName = "Account Security",
            I18nKey = $"{I18nBase}.groups.identitySecurity",
            Icon = "mdi:shield-account-outline",
            Order = 200,
            Fields =
            [
                // --- Account Lockout（AuthService 每次登录热读，作为 lockoutOnFailure 实参） ---
                Field("Identity:AccountSecurity:EnableLockout", "Enable Account Lockout", "enableLockout",
                    SettingFieldType.Boolean, defaultAccessor:
                    () => new AccountSecurityOptions().EnableLockout.ToString().ToLowerInvariant()),
            ],
        },
        new SettingDefinitionGroup
        {
            Key = "identity-registration",
            ModuleName = "Identity",
            DisplayName = "Registration & Sign-in",
            I18nKey = $"{I18nBase}.groups.identityRegistration",
            Icon = "mdi:account-plus-outline",
            Order = 210,
            Fields =
            [
                // --- Registration (consumed by AuthService + RegistrationService) ---
                Field("Identity:Registration:EnableQuickRegisterEmail", "Enable Quick Register (Email)", "enableQuickRegisterEmail",
                    SettingFieldType.Boolean, defaultAccessor:
                    () => new RegistrationOptions().EnableQuickRegisterEmail.ToString().ToLowerInvariant()),
                Field("Identity:Registration:EnableQuickRegisterSms", "Enable Quick Register (SMS)", "enableQuickRegisterSms",
                    SettingFieldType.Boolean, defaultAccessor:
                    () => new RegistrationOptions().EnableQuickRegisterSms.ToString().ToLowerInvariant()),
                Field("Identity:Registration:RequireConfirmedEmail", "Require Email Confirmation", "requireConfirmedEmail",
                    SettingFieldType.Boolean, defaultAccessor:
                    () => new RegistrationOptions().RequireConfirmedEmail.ToString().ToLowerInvariant()),
                Field("Identity:Registration:RequireConfirmedPhone", "Require Phone Confirmation", "requireConfirmedPhone",
                    SettingFieldType.Boolean, defaultAccessor:
                    () => new RegistrationOptions().RequireConfirmedPhone.ToString().ToLowerInvariant()),

                // --- Sign-in (consumed by AuthService) ---
                Field("Identity:SignIn:AllowUserNameLogin", "Allow Username Login", "allowUserNameLogin",
                    SettingFieldType.Boolean, defaultAccessor:
                    () => new TnziSignInOptions().AllowUserNameLogin.ToString().ToLowerInvariant()),
                Field("Identity:SignIn:AllowEmailLogin", "Allow Email Login", "allowEmailLogin",
                    SettingFieldType.Boolean, defaultAccessor:
                    () => new TnziSignInOptions().AllowEmailLogin.ToString().ToLowerInvariant()),
                Field("Identity:SignIn:AllowSmsLogin", "Allow SMS Login", "allowSmsLogin",
                    SettingFieldType.Boolean, defaultAccessor:
                    () => new TnziSignInOptions().AllowSmsLogin.ToString().ToLowerInvariant()),
            ],
        },
    ];

    private static SettingFieldDefinition Field(
        string key,
        string label,
        string i18nSuffix,
        SettingFieldType type = SettingFieldType.String,
        double? min = null,
        double? max = null,
        Func<string?>? defaultAccessor = null) => new()
    {
        Key = key,
        Label = label,
        I18nKey = $"{I18nBase}.fields.{i18nSuffix}",
        Type = type,
        Min = min,
        Max = max,
        DefaultValueAccessor = defaultAccessor,
    };
}
