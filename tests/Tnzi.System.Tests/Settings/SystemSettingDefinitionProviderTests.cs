namespace Tnzi.System.Tests.Settings;

/// <summary>
/// 行为锁定测试 - 验证 AttributeSettingDefinitionProvider（通过 AppDomain 扫描）仍生成等价的 system-general 组。
/// </summary>
public class SystemSettingDefinitionProviderTests
{
    [Fact]
    public void GetGroups_Should_Define_General_Group_With_ApplicationOptions_Fields()
    {
        var groups = new AttributeSettingDefinitionProvider().GetGroups();

        var general = groups.Single(g => g.Key == "system-general");
        general.ModuleName.ShouldBe("System");
        general.Fields.Select(f => f.Key).ShouldContain("System:SiteName");
        general.Fields.Select(f => f.Key).ShouldContain("System:LogoUrl");
        general.Fields.ShouldAllBe(f => f.Key.StartsWith("System:"));
        // 与 ApplicationOptions 的 12 个属性一一对应 - 新增/删除属性时必须同步本组
        general.Fields.Count.ShouldBe(12);
    }

    [Fact]
    public void GetGroups_Compiled_Default_Accessors_Should_Return_Values()
    {
        var groups = new AttributeSettingDefinitionProvider().GetGroups();
        var general = groups.Single(g => g.Key == "system-general");

        var siteName = general.Fields.Single(f => f.Key == "System:SiteName");
        siteName.DefaultValueAccessor.ShouldNotBeNull();
        siteName.DefaultValueAccessor!().ShouldNotBeNullOrWhiteSpace();
        var appName = general.Fields.Single(f => f.Key == "System:AppName");
        appName.DefaultValueAccessor.ShouldNotBeNull();
        appName.DefaultValueAccessor!().ShouldNotBeNullOrWhiteSpace();
    }
}
