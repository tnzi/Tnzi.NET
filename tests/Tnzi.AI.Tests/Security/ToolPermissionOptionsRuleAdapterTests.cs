namespace Tnzi.AI.Tests.Security;

public class ToolPermissionOptionsRuleAdapterTests
{
    [Fact]
    public void ToRules_DisabledOptions_ReturnsEmpty()
    {
        var rules = ToolPermissionOptionsRuleAdapter.ToRules(new ToolPermissionOptions { Enabled = false });

        rules.ShouldBeEmpty();
    }

    [Fact]
    public void ToRules_AssignsScopesForAllRuleGroups()
    {
        var options = new ToolPermissionOptions
        {
            Enabled = true,
            SystemRules =
            [
                new ToolPermissionRuleOptions { ToolPattern = "bash", Behavior = PermissionBehavior.Deny }
            ],
            ProjectRules =
            [
                new ToolPermissionRuleOptions { ToolPattern = "write_file", Behavior = PermissionBehavior.Ask }
            ],
            UserRules =
            [
                new ToolPermissionRuleOptions { ToolPattern = "read_file", Behavior = PermissionBehavior.Allow }
            ],
            SessionRules =
            [
                new ToolPermissionRuleOptions { ToolPattern = "mcp:*", Behavior = PermissionBehavior.Deny }
            ]
        };

        var rules = ToolPermissionOptionsRuleAdapter.ToRules(options);

        rules.Count.ShouldBe(4);
        rules.Single(x => x.ToolPattern == "bash").Scope.ShouldBe(ToolPermissionScope.System);
        rules.Single(x => x.ToolPattern == "write_file").Scope.ShouldBe(ToolPermissionScope.Project);
        rules.Single(x => x.ToolPattern == "read_file").Scope.ShouldBe(ToolPermissionScope.User);
        rules.Single(x => x.ToolPattern == "mcp:*").Scope.ShouldBe(ToolPermissionScope.Session);
    }

    [Fact]
    public void ToRules_TrimsOptionalFields()
    {
        var options = new ToolPermissionOptions
        {
            Enabled = true,
            SessionRules =
            [
                new ToolPermissionRuleOptions
                {
                    ToolPattern = "  bash  ",
                    ToolGroup = " shell ",
                    CommandPrefix = " rm ",
                    ServerName = " filesystem ",
                    PathPrefix = " D:\\My\\Tnzi.NET\\src ",
                    SubAgentName = " SearchAgent ",
                    WorkflowNodeName = " approval-step ",
                    Reason = " blocked "
                }
            ]
        };

        var rule = ToolPermissionOptionsRuleAdapter.ToRules(options).Single();

        rule.ToolPattern.ShouldBe("bash");
        rule.ToolGroup.ShouldBe("shell");
        rule.CommandPrefix.ShouldBe("rm");
        rule.ServerName.ShouldBe("filesystem");
        rule.PathPrefix.ShouldBe("D:\\My\\Tnzi.NET\\src");
        rule.SubAgentName.ShouldBe("SearchAgent");
        rule.WorkflowNodeName.ShouldBe("approval-step");
        rule.Reason.ShouldBe("blocked");
    }
}
