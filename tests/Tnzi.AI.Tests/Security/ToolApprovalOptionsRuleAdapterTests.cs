namespace Tnzi.AI.Tests.Security;

public class ToolApprovalOptionsRuleAdapterTests
{
    [Fact]
    public void ToRules_TrimsAndDeduplicatesConfiguredNames()
    {
        var rules = ToolApprovalOptionsRuleAdapter.ToRules(new ToolApprovalOptions
        {
            Enabled = true,
            Mode = ToolApprovalMode.Specific,
            AlwaysRequireApproval =
            [
                " bash ",
                "bash"
            ],
            AlwaysRequireApprovalGroups =
            [
                " shell ",
                "shell"
            ],
            NeverRequireApproval =
            [
                " read_file ",
                "read_file"
            ]
        });

        rules.Count.ShouldBe(3);

        var toolRule = rules.Single(x => x.ToolPattern == "bash");
        toolRule.Behavior.ShouldBe(PermissionBehavior.Ask);

        var groupRule = rules.Single(x => x.ToolGroup == "shell");
        groupRule.ToolPattern.ShouldBe("*");
        groupRule.Behavior.ShouldBe(PermissionBehavior.Ask);

        var allowRule = rules.Single(x => x.ToolPattern == "read_file");
        allowRule.Behavior.ShouldBe(PermissionBehavior.Allow);
    }
}
