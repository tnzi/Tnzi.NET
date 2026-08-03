
namespace Tnzi.AI.Tests.Security;

public class ToolPermissionTests
{
    [Fact]
    public void Evaluate_NoDenyRules_AllowsByDefault()
    {
        // Arrange
        var evaluator = new ToolPermissionEvaluator([]);

        // Act
        var decision = evaluator.Evaluate("any_tool");

        // Assert
        decision.Behavior.ShouldBe(PermissionBehavior.Allow);
        decision.ToolName.ShouldBe("any_tool");
    }

    [Fact]
    public void Evaluate_DenyRule_ReturnsAndOverridesAllow()
    {
        // Arrange - both deny and allow exist for the same tool, deny wins
        var rules = new List<ToolPermissionRule>
        {
            new() { ToolPattern = "dangerous_tool", Behavior = PermissionBehavior.Allow },
            new() { ToolPattern = "dangerous_tool", Behavior = PermissionBehavior.Deny, Reason = "Blocked by policy" }
        };
        var evaluator = new ToolPermissionEvaluator(rules);

        // Act
        var decision = evaluator.Evaluate("dangerous_tool");

        // Assert
        decision.Behavior.ShouldBe(PermissionBehavior.Deny);
        decision.Reason.ShouldBe("Blocked by policy");
    }

    [Fact]
    public void Evaluate_AskRule_ReturnsAsk()
    {
        var rules = new List<ToolPermissionRule>
        {
            new() { ToolPattern = "shell_exec", Behavior = PermissionBehavior.Ask, Reason = "Shell execution requires approval" }
        };
        var evaluator = new ToolPermissionEvaluator(rules);

        var decision = evaluator.Evaluate("shell_exec");

        decision.Behavior.ShouldBe(PermissionBehavior.Ask);
        decision.Reason.ShouldBe("Shell execution requires approval");
        decision.RequiresApprovalHandler.ShouldBeTrue();
    }

    [Fact]
    public void Evaluate_DestructiveTool_WithoutExplicitAllow_Denied()
    {
        // Arrange - no rules at all, destructive tool should be denied
        var evaluator = new ToolPermissionEvaluator([]);

        // Act
        var decision = evaluator.Evaluate("delete_everything", isDestructive: true);

        // Assert
        decision.Behavior.ShouldBe(PermissionBehavior.Deny);
        decision.Reason.ShouldBe("Destructive tool requires explicit allow");
    }

    [Fact]
    public void Evaluate_DestructiveTool_WithExplicitAllow_Allowed()
    {
        // Arrange - explicit allow rule for the destructive tool
        var rules = new List<ToolPermissionRule>
        {
            new() { ToolPattern = "delete_everything", Behavior = PermissionBehavior.Allow }
        };
        var evaluator = new ToolPermissionEvaluator(rules);

        // Act
        var decision = evaluator.Evaluate("delete_everything", isDestructive: true);

        // Assert
        decision.Behavior.ShouldBe(PermissionBehavior.Allow);
    }

    [Fact]
    public void Evaluate_DestructiveTool_WithExplicitDeny_PreservesSpecificReason()
    {
        var evaluator = new ToolPermissionEvaluator(
        [
            new ToolPermissionRule
            {
                ToolPattern = "delete_everything",
                Behavior = PermissionBehavior.Deny,
                Reason = "Blocked by destructive policy"
            }
        ]);

        var decision = evaluator.Evaluate("delete_everything", isDestructive: true);

        decision.Behavior.ShouldBe(PermissionBehavior.Deny);
        decision.Reason.ShouldBe("Blocked by destructive policy");
    }

    [Fact]
    public void Evaluate_WildcardPattern_MatchesAll()
    {
        // Arrange - wildcard deny blocks everything
        var rules = new List<ToolPermissionRule>
        {
            new() { ToolPattern = "*", Behavior = PermissionBehavior.Deny, Reason = "All tools blocked" }
        };
        var evaluator = new ToolPermissionEvaluator(rules);

        // Act
        var decision = evaluator.Evaluate("any_tool_name");

        // Assert
        decision.Behavior.ShouldBe(PermissionBehavior.Deny);
        decision.Reason.ShouldBe("All tools blocked");
    }

    [Fact]
    public void Evaluate_PrefixWildcard_MatchesPrefix()
    {
        // Arrange - "mcp_*" matches "mcp_server_tool"
        var rules = new List<ToolPermissionRule>
        {
            new() { ToolPattern = "mcp_*", Behavior = PermissionBehavior.Deny, Reason = "MCP tools blocked" }
        };
        var evaluator = new ToolPermissionEvaluator(rules);

        // Act
        var matchDecision = evaluator.Evaluate("mcp_server_tool");
        var noMatchDecision = evaluator.Evaluate("other_tool");

        // Assert
        matchDecision.Behavior.ShouldBe(PermissionBehavior.Deny);
        matchDecision.Reason.ShouldBe("MCP tools blocked");
        noMatchDecision.Behavior.ShouldBe(PermissionBehavior.Allow);
    }

    [Fact]
    public void Evaluate_CaseInsensitive()
    {
        // Arrange - rule with lowercase pattern, tool with mixed case
        var rules = new List<ToolPermissionRule>
        {
            new() { ToolPattern = "my_tool", Behavior = PermissionBehavior.Deny, Reason = "Blocked" }
        };
        var evaluator = new ToolPermissionEvaluator(rules);

        // Act
        var decision = evaluator.Evaluate("MY_TOOL");

        // Assert
        decision.Behavior.ShouldBe(PermissionBehavior.Deny);
        decision.Reason.ShouldBe("Blocked");
    }

    [Fact]
    public void Evaluate_DenyRuleWithDefaultReason_UsesFallback()
    {
        // Arrange - deny rule without explicit reason
        var rules = new List<ToolPermissionRule>
        {
            new() { ToolPattern = "blocked_tool", Behavior = PermissionBehavior.Deny }
        };
        var evaluator = new ToolPermissionEvaluator(rules);

        // Act
        var decision = evaluator.Evaluate("blocked_tool");

        // Assert
        decision.Behavior.ShouldBe(PermissionBehavior.Deny);
        decision.Reason.ShouldBe("Denied by rule");
    }

    [Fact]
    public void Evaluate_PrefixWildcard_CaseInsensitive()
    {
        // Arrange - prefix wildcard with different casing
        var rules = new List<ToolPermissionRule>
        {
            new() { ToolPattern = "MCP_*", Behavior = PermissionBehavior.Deny }
        };
        var evaluator = new ToolPermissionEvaluator(rules);

        // Act
        var decision = evaluator.Evaluate("mcp_server_fetch");

        // Assert
        decision.Behavior.ShouldBe(PermissionBehavior.Deny);
    }

    [Fact]
    public void Evaluate_ToolGroupRule_MatchesContextGroup()
    {
        var rules = new List<ToolPermissionRule>
        {
            new() { ToolPattern = "*", ToolGroup = "mcp:filesystem", Behavior = PermissionBehavior.Ask, Reason = "Filesystem MCP requires approval" }
        };
        var evaluator = new ToolPermissionEvaluator(rules);

        var decision = evaluator.Evaluate(new ToolPermissionContext
        {
            ToolName = "delete_file",
            ToolGroup = "mcp:filesystem"
        });

        decision.Behavior.ShouldBe(PermissionBehavior.Ask);
        decision.MatchedToolGroup.ShouldBe("mcp:filesystem");
    }

    [Fact]
    public void Evaluate_CommandPrefixRule_MatchesShellSegment()
    {
        var rules = new List<ToolPermissionRule>
        {
            new() { ToolPattern = "bash", ToolGroup = "shell", CommandPrefix = "rm", Behavior = PermissionBehavior.Deny, Reason = "rm blocked" }
        };
        var evaluator = new ToolPermissionEvaluator(rules);
        var analysis = new ShellCommandAnalyzer().Analyze("git status && rm temp.txt");

        var decision = evaluator.Evaluate(new ToolPermissionContext
        {
            ToolName = "bash",
            ToolGroup = "shell",
            ShellCommand = "git status && rm temp.txt",
            ShellSegments = analysis.Segments.Select(x => x.CommandText).ToList(),
            IsDestructive = analysis.IsDestructiveCandidate
        });

        decision.Behavior.ShouldBe(PermissionBehavior.Deny);
        decision.Reason.ShouldBe("rm blocked");
    }

    [Fact]
    public void Evaluate_AdditionalRules_AreApplied()
    {
        var evaluator = new ToolPermissionEvaluator([]);

        var decision = evaluator.Evaluate(
            new ToolPermissionContext
            {
                ToolName = "sensitive_tool"
            },
            [
                new ToolPermissionRule
                {
                    ToolPattern = "sensitive_tool",
                    Behavior = PermissionBehavior.Deny,
                    Scope = ToolPermissionScope.Session,
                    Reason = "Blocked in current session"
                }
            ]);

        decision.Behavior.ShouldBe(PermissionBehavior.Deny);
        decision.Scope.ShouldBe(ToolPermissionScope.Session);
        decision.Reason.ShouldBe("Blocked in current session");
    }

    [Fact]
    public void Evaluate_HigherScopeAllow_OverridesLowerScopeDeny()
    {
        var evaluator = new ToolPermissionEvaluator(
        [
            new ToolPermissionRule
            {
                ToolPattern = "bash",
                Behavior = PermissionBehavior.Deny,
                Scope = ToolPermissionScope.System,
                Reason = "Blocked globally"
            },
            new ToolPermissionRule
            {
                ToolPattern = "bash",
                Behavior = PermissionBehavior.Allow,
                Scope = ToolPermissionScope.Session,
                Reason = "Allowed for current session"
            }
        ]);

        var decision = evaluator.Evaluate("bash");

        decision.Behavior.ShouldBe(PermissionBehavior.Allow);
        decision.Scope.ShouldBe(ToolPermissionScope.Session);
        decision.Reason.ShouldBe("Allowed for current session");
    }

    [Fact]
    public void Evaluate_ServerNameRule_MatchesMcpServerContext()
    {
        var evaluator = new ToolPermissionEvaluator(
        [
            new ToolPermissionRule
            {
                ToolPattern = "*",
                ToolGroup = "mcp:filesystem",
                ServerName = "filesystem",
                Behavior = PermissionBehavior.Deny,
                Reason = "Filesystem server blocked"
            }
        ]);

        var decision = evaluator.Evaluate(new ToolPermissionContext
        {
            ToolName = "mcp:filesystem/read_file",
            ToolGroup = "mcp:filesystem",
            ServerName = "filesystem"
        });

        decision.Behavior.ShouldBe(PermissionBehavior.Deny);
        decision.Reason.ShouldBe("Filesystem server blocked");
        decision.MatchedServerName.ShouldBe("filesystem");
    }

    [Fact]
    public void Evaluate_PathPrefixRule_MatchesCandidatePaths()
    {
        var evaluator = new ToolPermissionEvaluator(
        [
            new ToolPermissionRule
            {
                ToolPattern = "*",
                ToolGroup = "file",
                PathPrefix = "D:\\My\\Tnzi.NET\\src",
                Behavior = PermissionBehavior.Ask,
                Reason = "Source tree requires approval"
            }
        ]);

        var decision = evaluator.Evaluate(new ToolPermissionContext
        {
            ToolName = "write_file",
            ToolGroup = "file",
            CandidatePaths = ["C:/src/Tnzi.NET/src/Tnzi.AI/Tool.cs"]
        });

        decision.Behavior.ShouldBe(PermissionBehavior.Ask);
        decision.Reason.ShouldBe("Source tree requires approval");
        decision.MatchedPathPrefix.ShouldBe("D:\\My\\Tnzi.NET\\src");
    }

    [Fact]
    public void Evaluate_PathPrefixRule_DoesNotMatchSiblingPathWithSamePrefix()
    {
        var evaluator = new ToolPermissionEvaluator(
        [
            new ToolPermissionRule
            {
                ToolPattern = "*",
                ToolGroup = "file",
                PathPrefix = "D:\\My\\Tnzi.NET\\src",
                Behavior = PermissionBehavior.Ask,
                Reason = "Source tree requires approval"
            }
        ]);

        var decision = evaluator.Evaluate(new ToolPermissionContext
        {
            ToolName = "write_file",
            ToolGroup = "file",
            CandidatePaths = ["D:\\My\\Tnzi.NET\\src2\\Tool.cs"]
        });

        decision.Behavior.ShouldBe(PermissionBehavior.Allow);
        decision.MatchedPathPrefix.ShouldBeNull();
    }

    [Fact]
    public void Evaluate_SubAgentRule_MatchesSubAgentContext()
    {
        var evaluator = new ToolPermissionEvaluator(
        [
            new ToolPermissionRule
            {
                ToolPattern = "bash",
                ToolGroup = "shell",
                IsSubAgentOnly = true,
                SubAgentName = "SearchAgent",
                Behavior = PermissionBehavior.Deny,
                Reason = "Sub-agent shell blocked"
            }
        ]);

        var decision = evaluator.Evaluate(new ToolPermissionContext
        {
            ToolName = "bash",
            ToolGroup = "shell",
            IsSubAgent = true,
            SubAgentName = "SearchAgent"
        });

        decision.Behavior.ShouldBe(PermissionBehavior.Deny);
        decision.Reason.ShouldBe("Sub-agent shell blocked");
        decision.MatchedSubAgentName.ShouldBe("SearchAgent");
    }

    [Fact]
    public void Evaluate_WorkflowRule_MatchesWorkflowContext()
    {
        var evaluator = new ToolPermissionEvaluator(
        [
            new ToolPermissionRule
            {
                ToolPattern = "write_file",
                ToolGroup = "file",
                IsWorkflowOnly = true,
                Behavior = PermissionBehavior.Ask,
                Reason = "Workflow file writes require approval"
            }
        ]);

        var decision = evaluator.Evaluate(new ToolPermissionContext
        {
            ToolName = "write_file",
            ToolGroup = "file",
            IsWorkflowRun = true,
            WorkflowId = Guid.NewGuid()
        });

        decision.Behavior.ShouldBe(PermissionBehavior.Ask);
        decision.Reason.ShouldBe("Workflow file writes require approval");
    }

    [Fact]
    public void Evaluate_WorkflowNodeRule_MatchesWorkflowNodeContext()
    {
        var evaluator = new ToolPermissionEvaluator(
        [
            new ToolPermissionRule
            {
                ToolPattern = "write_file",
                ToolGroup = "file",
                IsWorkflowOnly = true,
                WorkflowNodeName = "approval-step",
                Behavior = PermissionBehavior.Deny,
                Reason = "Approval step cannot write files"
            }
        ]);

        var decision = evaluator.Evaluate(new ToolPermissionContext
        {
            ToolName = "write_file",
            ToolGroup = "file",
            IsWorkflowRun = true,
            WorkflowId = Guid.NewGuid(),
            WorkflowExecutionId = "wf-exec-001",
            WorkflowNodeName = "approval-step"
        });

        decision.Behavior.ShouldBe(PermissionBehavior.Deny);
        decision.Reason.ShouldBe("Approval step cannot write files");
        decision.MatchedWorkflowNodeName.ShouldBe("approval-step");
    }

    [Fact]
    public void Constructor_NullRules_Throws()
    {
        // Act & Assert
        Should.Throw<ArgumentNullException>(() => new ToolPermissionEvaluator(null!));
    }

    [Fact]
    public void Evaluate_NullToolName_Throws()
    {
        // Arrange
        var evaluator = new ToolPermissionEvaluator([]);

        // Act & Assert
        Should.Throw<ArgumentException>(() => evaluator.Evaluate((string)null!));
    }
}
