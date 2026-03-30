namespace Tnzi.AI.Tests.Guardrails;

/// <summary>
/// IGuardrailProvider 协议 + GuardrailDecision 模型单元测试
/// </summary>
public class GuardrailProviderProtocolTests
{
    [Fact]
    public void Allow_CreatesAllowedDecision()
    {
        var decision = GuardrailDecision.Allow();

        decision.IsAllowed.ShouldBeTrue();
        decision.Reasons.ShouldBeEmpty();
        decision.PolicyId.ShouldBeNull();
        decision.Metadata.ShouldBeNull();
    }

    [Fact]
    public void Deny_CreatesDeniedDecisionWithReason()
    {
        var decision = GuardrailDecision.Deny("pii_detected", "Email address found");

        decision.IsAllowed.ShouldBeFalse();
        decision.Reasons.Count.ShouldBe(1);
        decision.Reasons[0].Code.ShouldBe("pii_detected");
        decision.Reasons[0].Message.ShouldBe("Email address found");
    }

    [Fact]
    public void Deny_WithPolicyIdAndMetadata()
    {
        var metadata = new Dictionary<string, object> { ["severity"] = "high" };
        var decision = GuardrailDecision.Deny("tool_denied", "Blocked by policy", "security-policy-v1", metadata);

        decision.IsAllowed.ShouldBeFalse();
        decision.PolicyId.ShouldBe("security-policy-v1");
        decision.Metadata.ShouldNotBeNull();
        decision.Metadata!["severity"].ShouldBe("high");
    }

    [Fact]
    public void GuardrailRequest_DefaultTimestamp_IsUtcNow()
    {
        var before = DateTimeOffset.UtcNow;
        var request = new GuardrailRequest { Content = "test" };
        var after = DateTimeOffset.UtcNow;

        request.Timestamp.ShouldBeGreaterThanOrEqualTo(before);
        request.Timestamp.ShouldBeLessThanOrEqualTo(after);
    }

    [Fact]
    public void GuardrailRequest_ToolRequest_HasToolFields()
    {
        var request = new GuardrailRequest
        {
            ToolName = "bash",
            ToolInput = new Dictionary<string, object> { ["command"] = "ls" },
            AgentId = Guid.NewGuid(),
            ThreadId = Guid.NewGuid()
        };

        request.ToolName.ShouldBe("bash");
        request.ToolInput.ShouldContainKey("command");
        request.AgentId.ShouldNotBeNull();
        request.ThreadId.ShouldNotBeNull();
        request.Content.ShouldBeNull();
    }

    [Fact]
    public void GuardrailReason_RecordEquality()
    {
        var r1 = new GuardrailReason("code1", "message1");
        var r2 = new GuardrailReason("code1", "message1");

        r1.ShouldBe(r2);
    }
}
