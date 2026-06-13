using Tnzi.Identity.Settings;
using Tnzi.Settings;

namespace Tnzi.Identity.Tests.Settings;

public class IdentitySettingDefinitionProviderTests
{
    private readonly IReadOnlyList<SettingDefinitionGroup> _groups;

    public IdentitySettingDefinitionProviderTests()
    {
        _groups = new IdentitySettingDefinitionProvider().GetGroups();
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
        security.Order.ShouldBe(200);
        security.Icon.ShouldBe("mdi:shield-account-outline");
        // PasswordPolicy 5 字段与 MaxFailedLoginAttempts/LockoutDurationMinutes 已移除（双轨冻结，见 provider 注释）
        security.Fields.Count.ShouldBe(1);
    }

    [Fact]
    public void Registration_Group_Has_Correct_Structure()
    {
        var registration = _groups.Single(g => g.Key == "identity-registration");

        registration.DisplayName.ShouldBe("Registration & Sign-in");
        registration.Order.ShouldBe(210);
        registration.Icon.ShouldBe("mdi:account-plus-outline");
        registration.Fields.Count.ShouldBe(7);
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
        field.DefaultValueAccessor!().ShouldBe("true");
    }

    [Fact]
    public void EnableQuickRegisterEmail_Has_Correct_Default()
    {
        var registration = _groups.Single(g => g.Key == "identity-registration");
        var field = registration.Fields.Single(f => f.Key == "Identity:Registration:EnableQuickRegisterEmail");

        field.Type.ShouldBe(SettingFieldType.Boolean);
        field.DefaultValueAccessor!().ShouldBe("false");
    }

    [Fact]
    public void AllowEmailLogin_Has_Correct_Default()
    {
        var registration = _groups.Single(g => g.Key == "identity-registration");
        var field = registration.Fields.Single(f => f.Key == "Identity:SignIn:AllowEmailLogin");

        field.Type.ShouldBe(SettingFieldType.Boolean);
        field.DefaultValueAccessor!().ShouldBe("true");
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
