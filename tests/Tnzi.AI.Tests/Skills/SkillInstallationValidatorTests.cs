namespace Tnzi.AI.Tests.Skills;

/// <summary>
/// SkillInstallationValidator 安全验证测试
/// </summary>
public class SkillInstallationValidatorTests
{
    // -------------------------------------------------------------------------
    // Name Validation
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("my-skill")]
    [InlineData("a")]
    [InlineData("web-search-v2")]
    [InlineData("abc123")]
    public void ValidateName_ValidNames_ReturnsSuccess(string name)
    {
        var result = SkillInstallationValidator.ValidateName(name);
        result.IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData("My Skill")]       // spaces
    [InlineData("MY-SKILL")]       // uppercase
    [InlineData("-leading")]       // leading hyphen
    [InlineData("trailing-")]      // trailing hyphen
    [InlineData("has--double")]    // double hyphen
    [InlineData("has_underscore")] // underscore
    public void ValidateName_InvalidNames_ReturnsFailure(string name)
    {
        var result = SkillInstallationValidator.ValidateName(name);
        result.IsValid.ShouldBeFalse();
        result.ErrorMessage.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void ValidateName_TooLong_ReturnsFailure()
    {
        var longName = string.Join("-", Enumerable.Repeat("a", 33)); // > 64 chars
        var result = SkillInstallationValidator.ValidateName(longName);
        result.IsValid.ShouldBeFalse();
        var errorMessage = result.ErrorMessage;
        errorMessage.ShouldNotBeNull();
        errorMessage.ShouldContain("maximum length");
    }

    // -------------------------------------------------------------------------
    // Description Validation
    // -------------------------------------------------------------------------

    [Fact]
    public void ValidateDescription_Valid_ReturnsSuccess()
    {
        var result = SkillInstallationValidator.ValidateDescription("A useful skill for searching the web");
        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void ValidateDescription_WithAngleBrackets_ReturnsFailure()
    {
        var result = SkillInstallationValidator.ValidateDescription("Inject <script>alert(1)</script>");
        result.IsValid.ShouldBeFalse();
        var errorMessage = result.ErrorMessage;
        errorMessage.ShouldNotBeNull();
        errorMessage.ShouldContain("angle brackets");
    }

    [Fact]
    public void ValidateDescription_TooLong_ReturnsFailure()
    {
        var longDesc = new string('a', 1025);
        var result = SkillInstallationValidator.ValidateDescription(longDesc);
        result.IsValid.ShouldBeFalse();
        var errorMessage = result.ErrorMessage;
        errorMessage.ShouldNotBeNull();
        errorMessage.ShouldContain("maximum length");
    }

    // -------------------------------------------------------------------------
    // Frontmatter Validation
    // -------------------------------------------------------------------------

    [Fact]
    public void ValidateFrontmatter_AllowedKeys_ReturnsSuccess()
    {
        var fm = new Dictionary<string, string>
        {
            ["name"] = "test",
            ["description"] = "Test skill",
            ["version"] = "1.0",
            ["tags"] = "search,ai",
            ["author"] = "test"
        };

        var result = SkillInstallationValidator.ValidateFrontmatter(fm);
        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void ValidateFrontmatter_UnknownKey_ReturnsFailure()
    {
        var fm = new Dictionary<string, string>
        {
            ["name"] = "test",
            ["malicious_key"] = "payload"
        };

        var result = SkillInstallationValidator.ValidateFrontmatter(fm);
        result.IsValid.ShouldBeFalse();
        var errorMessage = result.ErrorMessage;
        errorMessage.ShouldNotBeNull();
        errorMessage.ShouldContain("malicious_key");
    }
}
