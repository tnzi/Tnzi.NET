using Tnzi.Settings;
using Tnzi.Identity.Options;

namespace Tnzi.Identity.Tests.Settings;

public class IdentitySettingDefinitionProviderTests
{
    private readonly IReadOnlyList<SettingDefinitionGroup> _groups;

    public IdentitySettingDefinitionProviderTests()
    {
        // Extract per-class and merge groups sharing the same Key (mirrors AttributeSettingDefinitionProvider.MergeByGroupKey).
        var raw = new List<SettingDefinitionGroup>();
        foreach (var type in new[] { typeof(AccountSecurityOptions), typeof(RegistrationOptions), typeof(TnziSignInOptions) })
        {
            var g = RuntimeSettingMetadataExtractor.Extract(type);
            if (g != null) raw.Add(g);
        }

        var merged = raw
            .GroupBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(cluster =>
            {
                var first = cluster.First();
                if (cluster.Count() == 1) return first;
                return new SettingDefinitionGroup
                {
                    Key = first.Key,
                    ModuleName = first.ModuleName,
                    DisplayName = first.DisplayName,
                    I18nKey = first.I18nKey,
                    Icon = first.Icon,
                    Order = cluster.Min(c => c.Order),
                    Fields = cluster.SelectMany(c => c.Fields).ToList(),
                };
            })
            .ToList();

        _groups = merged;
    }

    [Fact]
    public void GetGroups_Returns_Two_Groups_With_Correct_Keys()
    {
        _groups.Count.ShouldBe(2);
        _groups.Select(g => g.Key).ShouldBe(["identity-security", "identity-registration"]);
    }

    [Fact]
    public void All_Groups_Belong_To_Identity_Module()
    {
        _groups.ShouldAllBe(g => g.ModuleName == "Identity");
    }

    [Fact]
    public void Security_Group_Has_Correct_Structure()
    {
        var security = _groups.Single(g => g.Key == "identity-security");

        security.DisplayName.ShouldBe("Account Security");
        security.Order.ShouldBe(210);
        security.Icon.ShouldBe("mdi:shield-account-outline");
        // MaxFailedLoginAttempts/LockoutDurationMinutes 仍双轨冻结（见 AccountSecurityOptions 注释）。
        // 暴露：EnableLockout + SessionTimeoutMinutes + EnableAbnormalLoginDetection + 5 风险等级 + 2 阈值 = 10。
        security.Fields.Count.ShouldBe(10);
    }

    [Fact]
    public void Registration_Group_Has_Correct_Structure()
    {
        var registration = _groups.Single(g => g.Key == "identity-registration");

        registration.DisplayName.ShouldBe("Registration & Sign-in");
        registration.Order.ShouldBe(200);
        registration.Icon.ShouldBe("mdi:account-plus-outline");
        // SignIn: UseEmailAsUserName + Allow{UserName,Email,Sms}Login = 4；
        // Registration: EnableQuickRegister{Email,Sms} + DefaultUserNameFromEmail + RequireConfirmed{Email,Phone} + SetPasswordTokenExpirationMinutes = 6 → 合计 10。
        registration.Fields.Count.ShouldBe(10);
    }

    [Fact]
    public void Security_Group_Fields_Have_Identity_Key_Prefix()
    {
        var security = _groups.Single(g => g.Key == "identity-security");
        security.Fields.ShouldAllBe(f => f.Key.StartsWith("Identity:"));
    }

    [Fact]
    public void Registration_Group_Fields_Have_Identity_Key_Prefix()
    {
        var registration = _groups.Single(g => g.Key == "identity-registration");
        registration.Fields.ShouldAllBe(f => f.Key.StartsWith("Identity:"));
    }

    [Fact]
    public void EnableLockout_Has_Correct_Default()
    {
        var security = _groups.Single(g => g.Key == "identity-security");
        var field = security.Fields.Single(f => f.Key == "Identity:AccountSecurity:EnableLockout");

        field.Type.ShouldBe(SettingFieldType.Boolean);
        field.DefaultValueAccessor!().ShouldBe("True");
    }

    [Fact]
    public void EnableQuickRegisterEmail_Has_Correct_Default()
    {
        var registration = _groups.Single(g => g.Key == "identity-registration");
        var field = registration.Fields.Single(f => f.Key == "Identity:Registration:EnableQuickRegisterEmail");

        field.Type.ShouldBe(SettingFieldType.Boolean);
        field.DefaultValueAccessor!().ShouldBe("False");
    }

    [Fact]
    public void AllowEmailLogin_Has_Correct_Default()
    {
        var registration = _groups.Single(g => g.Key == "identity-registration");
        var field = registration.Fields.Single(f => f.Key == "Identity:SignIn:AllowEmailLogin");

        field.Type.ShouldBe(SettingFieldType.Boolean);
        field.DefaultValueAccessor!().ShouldBe("True");
    }

    [Fact]
    public void All_Fields_Have_I18n_Keys()
    {
        _groups.SelectMany(g => g.Fields)
               .ShouldAllBe(f => f.I18nKey != null && f.I18nKey.StartsWith("admin.modules.system.settings.fields."));
    }

    [Fact]
    public void All_Groups_Have_I18n_Keys()
    {
        _groups.ShouldAllBe(g => g.I18nKey != null && g.I18nKey.StartsWith("admin.modules.system.settings.groups."));
    }
}
