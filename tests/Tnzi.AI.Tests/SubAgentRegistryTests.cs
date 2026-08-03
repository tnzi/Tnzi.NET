namespace Tnzi.AI.Tests;

public class SubAgentRegistryTests
{
    [Fact]
    public void BuiltInTypes_ExposeExtendedMetadata()
    {
        var registry = new SubAgentRegistry();

        var general = registry.Get("general-purpose");
        var bash = registry.Get("bash");
        var researcher = registry.Get("researcher");

        general.ShouldNotBeNull();
        general.DefaultApprovalMode.ShouldBe(ToolApprovalMode.Specific);
        general.CapabilityTags!.ShouldContain("implementation");

        bash.ShouldNotBeNull();
        bash.DefaultApprovalMode.ShouldBe(ToolApprovalMode.Specific);
        bash.CapabilityTags!.ShouldContain("sandbox");

        researcher.ShouldNotBeNull();
        researcher.DefaultApprovalMode.ShouldBe(ToolApprovalMode.NeverRequire);
        researcher.CapabilityTags!.ShouldContain("research");
    }
}
