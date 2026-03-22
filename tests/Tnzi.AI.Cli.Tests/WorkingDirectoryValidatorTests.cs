using Tnzi.AI.Cli.Options;
using Tnzi.AI.Cli.Validation;
using Tnzi.Exceptions;

namespace Tnzi.AI.Cli.Tests;

public class WorkingDirectoryValidatorTests
{
    [Fact]
    public void Validate_ShouldPass_WhenAllowedDirectoriesEmpty()
    {
        var validator = CreateValidator([]);
        validator.Validate("C:\\Any\\Path");
    }

    [Fact]
    public void Validate_ShouldPass_WhenPathMatchesPattern()
    {
        var validator = CreateValidator(["D:\\My\\*"]);
        validator.Validate("D:\\My\\music");
    }

    [Fact]
    public void Validate_ShouldPass_WhenPathMatchesNestedPattern()
    {
        var validator = CreateValidator(["D:\\My\\*"]);
        validator.Validate("D:\\My\\music\\src\\Api");
    }

    [Fact]
    public void Validate_ShouldThrow_WhenPathNotInWhitelist()
    {
        var validator = CreateValidator(["D:\\My\\*"]);
        Assert.Throws<ForbiddenException>(() => validator.Validate("C:\\Windows\\System32"));
    }

    [Fact]
    public void Validate_ShouldSupportMultiplePatterns()
    {
        var validator = CreateValidator(["D:\\My\\*", "C:\\Projects\\*"]);
        validator.Validate("C:\\Projects\\app");
    }

    [Fact]
    public void Validate_ShouldBeCaseInsensitive()
    {
        var validator = CreateValidator(["D:\\My\\*"]);
        validator.Validate("d:\\my\\Music");
    }

    private static WorkingDirectoryValidator CreateValidator(List<string> allowedDirs)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new CliOptions
        {
            AllowedDirectories = allowedDirs
        });
        return new WorkingDirectoryValidator(options);
    }
}
