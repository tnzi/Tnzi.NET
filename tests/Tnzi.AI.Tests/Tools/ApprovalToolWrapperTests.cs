namespace Tnzi.AI.Tests.Tools;

public class ApprovalToolWrapperTests
{
    /// <summary>路径规则测试用的"受保护根目录"，按运行平台取。</summary>
    /// <remarks>
    /// 这几个测试验的是<b>路径前缀规则 + 相对路径按工作目录解析</b>，不是 Windows 路径语法本身。
    /// 此前写死开发机的真实路径：在 Linux 上反斜杠不是目录分隔符，
    /// <c>Path.Combine(工作目录, 相对路径)</c> 拼出的形态与 PathPrefix 对不上，
    /// 规则不命中 → 工具被放行 → 断言拿到 <c>should-not-run</c>。
    /// 这从未被发现，因为后端 full-suite 在 CI 上一直挂在更早的 restore 阶段
    /// （消费应用的 NuGet.config 里有 Windows 本地源,runner 上不存在）。
    /// </remarks>
    private static readonly string ProtectedRoot =
        OperatingSystem.IsWindows() ? @"D:\repo\Framework" : "/repo/framework";

    /// <summary><see cref="ProtectedRoot"/> 下的路径，用当前平台的分隔符拼。</summary>
    private static string Under(params string[] parts)
        => Path.Combine([ProtectedRoot, .. parts]);

    [Fact]
    public async Task InvokeAsync_DenyDecision_ReturnsRejectedMessage_WithoutCallingHandler()
    {
        var inner = AIFunctionFactory.Create(() => "ok", "dangerous_tool", "Dangerous tool");
        var approvalHandler = new Mock<IToolApprovalHandler>(MockBehavior.Strict);
        var evaluator = new ToolPermissionEvaluator(
        [
            new ToolPermissionRule
            {
                ToolPattern = "dangerous_tool",
                Behavior = PermissionBehavior.Deny,
                Reason = "Blocked by policy"
            }
        ]);

        var wrapper = new ApprovalToolWrapper(
            inner,
            approvalHandler.Object,
            new ToolApprovalOptions(),
            evaluator);

        var result = await wrapper.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>()));

        result.ShouldBe("Tool call rejected: Blocked by policy");
        approvalHandler.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task InvokeAsync_AskDecision_RequestsApproval_AndInvokesInnerFunction()
    {
        var inner = AIFunctionFactory.Create(() => "approved-result", "shell_exec", "Shell execution");
        var approvalHandler = new Mock<IToolApprovalHandler>();
        approvalHandler
            .Setup(x => x.RequestApprovalAsync(It.IsAny<ToolApprovalRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ToolApprovalResult.Approve("tester"));

        var evaluator = new ToolPermissionEvaluator(
        [
            new ToolPermissionRule
            {
                ToolPattern = "shell_exec",
                Behavior = PermissionBehavior.Ask,
                Reason = "Approval required"
            }
        ]);

        var wrapper = new ApprovalToolWrapper(
            inner,
            approvalHandler.Object,
            new ToolApprovalOptions { Enabled = true },
            evaluator);

        var result = await wrapper.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>()));

        var text = result switch
        {
            JsonElement json => json.GetString(),
            string value => value,
            _ => result?.ToString()
        };

        text.ShouldBe("approved-result");
        approvalHandler.Verify(x => x.RequestApprovalAsync(
            It.Is<ToolApprovalRequest>(r => r.ToolName == "shell_exec"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_ShellCommandRule_UsesCommandArgumentsForDecision()
    {
        var inner = AIFunctionFactory.Create(
            (string command, string? working_directory) => "should-not-run",
            "bash",
            "Execute a shell command");

        var approvalHandler = new Mock<IToolApprovalHandler>(MockBehavior.Strict);
        var evaluator = new ToolPermissionEvaluator(
        [
            new ToolPermissionRule
            {
                ToolPattern = "bash",
                ToolGroup = "shell",
                CommandPrefix = "rm",
                Behavior = PermissionBehavior.Deny,
                Reason = "rm blocked"
            }
        ]);

        var wrapper = new ApprovalToolWrapper(
            inner,
            approvalHandler.Object,
            new ToolApprovalOptions(),
            evaluator,
            new ShellCommandAnalyzer(),
            toolGroup: "shell");

        var result = await wrapper.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>
        {
            ["command"] = "git status && rm temp.txt",
            ["working_directory"] = ProtectedRoot
        }));

        result.ShouldBe("Tool call rejected: rm blocked");
        approvalHandler.VerifyNoOtherCalls();
    }

    [Fact]
    public void Wrap_EvaluatorRulesWithoutApprovalOptions_StillWrapsFunction()
    {
        var tool = AIFunctionFactory.Create(() => "ok", "bash", "Execute a shell command");
        var wrapped = ApprovalToolWrapper.Wrap(
            [tool],
            approvalHandler: null,
            options: new ToolApprovalOptions { Enabled = false },
            permissionEvaluator: new ToolPermissionEvaluator(
            [
                new ToolPermissionRule
                {
                    ToolPattern = "bash",
                    Behavior = PermissionBehavior.Deny
                }
            ]));

        wrapped.Count.ShouldBe(1);
        wrapped[0].ShouldBeOfType<ApprovalToolWrapper>();
    }

    [Fact]
    public void Wrap_EmptyEvaluatorWithoutApprovalOptions_StillWrapsFunction()
    {
        var tool = AIFunctionFactory.Create(() => "ok", "bash", "Execute a shell command");
        var wrapped = ApprovalToolWrapper.Wrap(
            [tool],
            approvalHandler: null,
            options: new ToolApprovalOptions { Enabled = false },
            permissionEvaluator: new ToolPermissionEvaluator([]));

        wrapped.Count.ShouldBe(1);
        wrapped[0].ShouldBeOfType<ApprovalToolWrapper>();
    }

    [Fact]
    public async Task InvokeAsync_AskDecisionWithoutApprovalHandler_ReturnsConfigurationError()
    {
        var inner = AIFunctionFactory.Create(() => "should-not-run", "shell_exec", "Shell execution");
        var wrapper = new ApprovalToolWrapper(
            inner,
            approvalHandler: null,
            new ToolApprovalOptions { Enabled = true },
            new ToolPermissionEvaluator(
            [
                new ToolPermissionRule
                {
                    ToolPattern = "shell_exec",
                    Behavior = PermissionBehavior.Ask
                }
            ]));

        var result = await wrapper.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>()));

        result.ShouldBe("Tool call rejected: approval handler is not configured.");
    }

    [Fact]
    public async Task InvokeAsync_McpServerRule_UsesFunctionAdditionalProperties()
    {
        var inner = AIFunctionFactory.Create(
            () => "should-not-run",
            new AIFunctionFactoryOptions
            {
                Name = "mcp:filesystem/read_file",
                Description = "Read file from MCP filesystem server",
                AdditionalProperties = new Dictionary<string, object?>
                {
                    ["mcp.server"] = "filesystem",
                    ["mcp.originalName"] = "read_file"
                }
            });

        var approvalHandler = new Mock<IToolApprovalHandler>(MockBehavior.Strict);
        var evaluator = new ToolPermissionEvaluator(
        [
            new ToolPermissionRule
            {
                ToolPattern = "*",
                ToolGroup = "mcp:filesystem",
                ServerName = "filesystem",
                Behavior = PermissionBehavior.Deny,
                Reason = "Filesystem MCP blocked"
            }
        ]);

        var wrapper = new ApprovalToolWrapper(
            inner,
            approvalHandler.Object,
            new ToolApprovalOptions(),
            evaluator,
            toolGroup: "mcp:filesystem");

        var result = await wrapper.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>()));

        result.ShouldBe("Tool call rejected: Filesystem MCP blocked");
        approvalHandler.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task InvokeAsync_PathRule_UsesPathArgumentsForDecision()
    {
        var inner = AIFunctionFactory.Create(
            (string path) => "should-not-run",
            "write_file",
            "Write a file");

        var approvalHandler = new Mock<IToolApprovalHandler>(MockBehavior.Strict);
        var evaluator = new ToolPermissionEvaluator(
        [
            new ToolPermissionRule
            {
                ToolPattern = "write_file",
                ToolGroup = "file",
                PathPrefix = Under("src"),
                Behavior = PermissionBehavior.Deny,
                Reason = "Protected source tree"
            }
        ]);

        var wrapper = new ApprovalToolWrapper(
            inner,
            approvalHandler.Object,
            new ToolApprovalOptions(),
            evaluator,
            toolGroup: "file");

        var result = await wrapper.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>
        {
            ["path"] = Under("src", "Tnzi.AI", "test.txt")
        }));

        result.ShouldBe("Tool call rejected: Protected source tree");
        approvalHandler.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task InvokeAsync_PathRule_ResolvesRelativePathAgainstArgumentWorkingDirectory()
    {
        var inner = AIFunctionFactory.Create(
            (string path, string? working_directory) => "should-not-run",
            "write_file",
            "Write a file");

        var approvalHandler = new Mock<IToolApprovalHandler>(MockBehavior.Strict);
        var evaluator = new ToolPermissionEvaluator(
        [
            new ToolPermissionRule
            {
                ToolPattern = "write_file",
                ToolGroup = "file",
                PathPrefix = Under("src"),
                Behavior = PermissionBehavior.Deny,
                Reason = "Protected source tree"
            }
        ]);

        var wrapper = new ApprovalToolWrapper(
            inner,
            approvalHandler.Object,
            new ToolApprovalOptions(),
            evaluator,
            toolGroup: "file");

        var result = await wrapper.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>
        {
            ["path"] = Path.Combine("src", "Tnzi.AI", "test.txt"),
            ["working_directory"] = ProtectedRoot
        }));

        result.ShouldBe("Tool call rejected: Protected source tree");
        approvalHandler.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task InvokeAsync_PathRule_ResolvesRelativePathAgainstExecutionContextWorkingDirectoryFallback()
    {
        var inner = AIFunctionFactory.Create(
            (string path) => "should-not-run",
            "write_file",
            "Write a file");

        var approvalHandler = new Mock<IToolApprovalHandler>(MockBehavior.Strict);
        var evaluator = new ToolPermissionEvaluator(
        [
            new ToolPermissionRule
            {
                ToolPattern = "write_file",
                ToolGroup = "file",
                PathPrefix = Under("src"),
                Behavior = PermissionBehavior.Deny,
                Reason = "Protected source tree"
            }
        ]);

        var accessor = new AgentExecutionContextAccessor
        {
            CurrentRequest = new AgentRunRequest
            {
                Metadata = new Dictionary<string, object>
                {
                    ["working_directory"] = ProtectedRoot
                }
            }
        };

        var wrapper = new ApprovalToolWrapper(
            inner,
            approvalHandler.Object,
            new ToolApprovalOptions(),
            evaluator,
            executionContextAccessor: accessor,
            toolGroup: "file");

        var result = await wrapper.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>
        {
            ["path"] = Path.Combine("src", "Tnzi.AI", "test.txt")
        }));

        result.ShouldBe("Tool call rejected: Protected source tree");
        approvalHandler.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task InvokeAsync_PathRule_UsesExecutionContextWorkingDirectoryFallback()
    {
        var inner = AIFunctionFactory.Create(() => "should-not-run", "list_files", "List files");
        var approvalHandler = new Mock<IToolApprovalHandler>(MockBehavior.Strict);
        var evaluator = new ToolPermissionEvaluator(
        [
            new ToolPermissionRule
            {
                ToolPattern = "list_files",
                ToolGroup = "file",
                PathPrefix = Under("src"),
                Behavior = PermissionBehavior.Deny,
                Reason = "Protected working directory"
            }
        ]);

        var accessor = new AgentExecutionContextAccessor
        {
            CurrentRequest = new AgentRunRequest
            {
                Metadata = new Dictionary<string, object>
                {
                    ["working_directory"] = Under("src", "Tnzi.AI")
                }
            }
        };

        var wrapper = new ApprovalToolWrapper(
            inner,
            approvalHandler.Object,
            new ToolApprovalOptions(),
            evaluator,
            executionContextAccessor: accessor,
            toolGroup: "file");

        var result = await wrapper.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>()));

        result.ShouldBe("Tool call rejected: Protected working directory");
        approvalHandler.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task InvokeAsync_SubAgentRule_UsesExecutionContextProperties()
    {
        var inner = AIFunctionFactory.Create(() => "should-not-run", "bash", "Execute shell command");
        var approvalHandler = new Mock<IToolApprovalHandler>(MockBehavior.Strict);
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

        var accessor = new AgentExecutionContextAccessor();
        accessor.Properties[ContextPropertyKeys.IsSubAgent] = true;
        accessor.Properties[ContextPropertyKeys.SubAgentName] = "SearchAgent";

        var wrapper = new ApprovalToolWrapper(
            inner,
            approvalHandler.Object,
            new ToolApprovalOptions(),
            evaluator,
            executionContextAccessor: accessor,
            toolGroup: "shell");

        var result = await wrapper.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>()));

        result.ShouldBe("Tool call rejected: Sub-agent shell blocked");
        approvalHandler.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task InvokeAsync_WorkflowRule_UsesCurrentRequestWorkflowId()
    {
        var inner = AIFunctionFactory.Create(() => "should-not-run", "write_file", "Write a file");
        var approvalHandler = new Mock<IToolApprovalHandler>(MockBehavior.Strict);
        var evaluator = new ToolPermissionEvaluator(
        [
            new ToolPermissionRule
            {
                ToolPattern = "write_file",
                ToolGroup = "file",
                IsWorkflowOnly = true,
                Behavior = PermissionBehavior.Deny,
                Reason = "Workflow write blocked"
            }
        ]);

        var accessor = new AgentExecutionContextAccessor
        {
            CurrentRequest = new AgentRunRequest
            {
                WorkflowId = Guid.NewGuid()
            }
        };

        var wrapper = new ApprovalToolWrapper(
            inner,
            approvalHandler.Object,
            new ToolApprovalOptions(),
            evaluator,
            executionContextAccessor: accessor,
            toolGroup: "file");

        var result = await wrapper.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>()));

        result.ShouldBe("Tool call rejected: Workflow write blocked");
        approvalHandler.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task InvokeAsync_WorkflowNodeRule_UsesExecutionContextMetadata()
    {
        var inner = AIFunctionFactory.Create(() => "should-not-run", "write_file", "Write a file");
        var approvalHandler = new Mock<IToolApprovalHandler>(MockBehavior.Strict);
        var evaluator = new ToolPermissionEvaluator(
        [
            new ToolPermissionRule
            {
                ToolPattern = "write_file",
                ToolGroup = "file",
                IsWorkflowOnly = true,
                WorkflowNodeName = "approval-step",
                Behavior = PermissionBehavior.Deny,
                Reason = "Approval step write blocked"
            }
        ]);

        var accessor = new AgentExecutionContextAccessor
        {
            CurrentRequest = new AgentRunRequest
            {
                WorkflowId = Guid.NewGuid(),
                Metadata = new Dictionary<string, object>
                {
                    ["workflow_execution_id"] = "wf-exec-001",
                    ["node_name"] = "approval-step"
                }
            }
        };

        var wrapper = new ApprovalToolWrapper(
            inner,
            approvalHandler.Object,
            new ToolApprovalOptions(),
            evaluator,
            executionContextAccessor: accessor,
            toolGroup: "file");

        var result = await wrapper.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>()));

        result.ShouldBe("Tool call rejected: Approval step write blocked");
        approvalHandler.VerifyNoOtherCalls();
    }
}
