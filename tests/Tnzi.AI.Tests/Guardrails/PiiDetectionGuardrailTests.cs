namespace Tnzi.AI.Tests.Guardrails;

/// <summary>
/// PiiDetectionGuardrail 单元测试 — 验证 PII（个人身份信息）检测
/// </summary>
/// <remarks>
/// 注意：当前实现使用 Rejected 模式（拒绝包含 PII 的输入），而非 Modified 模式（脱敏后放行）。
/// 测试验证的是拒绝行为和 PII 类型识别。
/// </remarks>
public class PiiDetectionGuardrailTests
{
    private static IOptions<AIOptions> CreateOptions(Action<GuardrailsOptions>? configure = null)
    {
        var options = new AIOptions();
        configure?.Invoke(options.Guardrails);
        return Microsoft.Extensions.Options.Options.Create(options);
    }

    [Fact]
    public async Task NoPii_ReturnsAllowed()
    {
        // Arrange
        var guardrail = new PiiDetectionGuardrail(CreateOptions(g => g.EnablePiiDetection = true));

        // Act
        var result = await guardrail.ValidateAsync("Tell me about quantum computing algorithms");

        // Assert
        result.IsAllowed.ShouldBeTrue();
        result.ModifiedContent.ShouldBeNull();
    }

    [Fact]
    public async Task EmailAddress_Rejected()
    {
        // Arrange
        var guardrail = new PiiDetectionGuardrail(CreateOptions(g => g.EnablePiiDetection = true));

        // Act
        var result = await guardrail.ValidateAsync("My email is john.doe@example.com please contact me");

        // Assert
        result.IsAllowed.ShouldBeFalse();
        result.GuardrailName.ShouldBe(nameof(PiiDetectionGuardrail));
        result.Reason.ShouldContain("email");
    }

    [Fact]
    public async Task PhoneNumber_Rejected()
    {
        // Arrange: 中国手机号格式 (1[3-9]开头 + 9 位数字)
        var guardrail = new PiiDetectionGuardrail(CreateOptions(g => g.EnablePiiDetection = true));

        // Act
        var result = await guardrail.ValidateAsync("Call me at 13912345678 anytime");

        // Assert
        result.IsAllowed.ShouldBeFalse();
        result.GuardrailName.ShouldBe(nameof(PiiDetectionGuardrail));
        result.Reason.ShouldContain("phone");
    }

    [Fact]
    public async Task CreditCardNumber_Rejected()
    {
        // Arrange: 信用卡号格式（带空格分隔的 16 位数字）
        var guardrail = new PiiDetectionGuardrail(CreateOptions(g => g.EnablePiiDetection = true));

        // Act
        var result = await guardrail.ValidateAsync("My card number is 4111 1111 1111 1111");

        // Assert
        result.IsAllowed.ShouldBeFalse();
        result.GuardrailName.ShouldBe(nameof(PiiDetectionGuardrail));
        result.Reason.ShouldContain("credit_card");
    }

    [Fact]
    public async Task MultiplePiiTypes_RejectsOnFirstMatch()
    {
        // Arrange: 包含多种 PII — 实现按顺序检查，第一个匹配即拒绝
        var guardrail = new PiiDetectionGuardrail(CreateOptions(g => g.EnablePiiDetection = true));

        // Act: email 在 PiiPatterns 中排第一
        var result = await guardrail.ValidateAsync(
            "Contact user@test.com or call 13812345678, card: 4111-1111-1111-1111");

        // Assert
        result.IsAllowed.ShouldBeFalse();
        result.GuardrailName.ShouldBe(nameof(PiiDetectionGuardrail));
        // 第一个匹配的是 email
        result.Reason.ShouldContain("email");
    }
}
