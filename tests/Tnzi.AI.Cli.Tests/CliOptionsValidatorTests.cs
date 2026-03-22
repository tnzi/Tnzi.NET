using Microsoft.Extensions.Options;
using Tnzi.AI.Cli.Options;

namespace Tnzi.AI.Cli.Tests;

public class CliOptionsValidatorTests
{
    [Fact]
    public void Validate_ShouldPass_WhenDefaultProviderExists()
    {
        var options = new CliOptions
        {
            DefaultProvider = "claude-code",
            Providers = new() { ["claude-code"] = new CliProviderOptions { Command = "claude" } }
        };
        var result = ValidateOptions(options);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_ShouldFail_WhenDefaultProviderNotInProviders()
    {
        var options = new CliOptions
        {
            DefaultProvider = "nonexistent",
            Providers = new() { ["claude-code"] = new CliProviderOptions { Command = "claude" } }
        };
        var result = ValidateOptions(options);
        Assert.True(result.Failed);
    }

    [Fact]
    public void Validate_ShouldFail_WhenProviderCommandEmpty()
    {
        var options = new CliOptions
        {
            DefaultProvider = "claude-code",
            Providers = new() { ["claude-code"] = new CliProviderOptions { Command = "" } }
        };
        var result = ValidateOptions(options);
        Assert.True(result.Failed);
    }

    [Fact]
    public void Validate_ShouldPass_WhenDefaultProviderEmpty()
    {
        var options = new CliOptions
        {
            DefaultProvider = "",
            Providers = new() { ["claude-code"] = new CliProviderOptions { Command = "claude" } }
        };
        var result = ValidateOptions(options);
        Assert.True(result.Succeeded);
    }

    private static ValidateOptionsResult ValidateOptions(CliOptions options)
    {
        var validator = new CliOptionsValidator();
        return validator.Validate(null, options);
    }
}
