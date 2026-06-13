namespace Tnzi.System.Tests.Settings;

public class SystemSettingDefinitionProviderTests
{
    [Fact]
    public void GetGroups_Should_Define_General_Group_With_ApplicationOptions_Fields()
    {
        var groups = new SystemSettingDefinitionProvider().GetGroups();

        var general = groups.ShouldHaveSingleItem();
        general.Key.ShouldBe("system-general");
        general.ModuleName.ShouldBe("System");
        general.Fields.Select(f => f.Key).ShouldContain("System:SiteName");
        general.Fields.Select(f => f.Key).ShouldContain("System:LogoUrl");
        general.Fields.ShouldAllBe(f => f.Key.StartsWith("System:"));
        // 与 ApplicationOptions 的 12 个属性一一对应 — 新增/删除属性时必须同步本组
        general.Fields.Count.ShouldBe(12);
    }

    [Fact]
    public void GetGroups_Compiled_Default_Accessors_Should_Return_Values()
    {
        var general = new SystemSettingDefinitionProvider().GetGroups().Single();

        var siteName = general.Fields.Single(f => f.Key == "System:SiteName");
        siteName.DefaultValueAccessor.ShouldNotBeNull();
        siteName.DefaultValueAccessor!().ShouldNotBeNullOrWhiteSpace();
        var appName = general.Fields.Single(f => f.Key == "System:AppName");
        appName.DefaultValueAccessor.ShouldNotBeNull();
        appName.DefaultValueAccessor!().ShouldNotBeNullOrWhiteSpace();
    }
}
