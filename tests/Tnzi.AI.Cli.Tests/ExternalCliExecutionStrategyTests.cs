using Tnzi.AI.Cli.Engine;
using Tnzi.AI.Cli.Options;
using Tnzi.AI.Dtos;
using Tnzi.Exceptions;

namespace Tnzi.AI.Cli.Tests;

public class ExternalCliExecutionStrategyTests
{
    // ── ResolveWorkingDirectory ──

    [Fact]
    public void ResolveWorkingDirectory_Metadata_TakesPriority()
    {
        var metadata = new Dictionary<string, object>
        {
            [CliConstants.WorkingDirectoryKey] = "/from/metadata"
        };
        var config = new ExternalCliExecutionConfigDto { WorkingDirectory = "/from/config" };
        var providerOptions = new CliProviderOptions { DefaultWorkingDirectory = "/from/provider" };

        var result = ExternalCliExecutionStrategy.ResolveWorkingDirectory(metadata, config, providerOptions);

        result.ShouldBe("/from/metadata");
    }

    [Fact]
    public void ResolveWorkingDirectory_Config_WhenNoMetadata()
    {
        var config = new ExternalCliExecutionConfigDto { WorkingDirectory = "/from/config" };
        var providerOptions = new CliProviderOptions { DefaultWorkingDirectory = "/from/provider" };

        var result = ExternalCliExecutionStrategy.ResolveWorkingDirectory(null, config, providerOptions);

        result.ShouldBe("/from/config");
    }

    [Fact]
    public void ResolveWorkingDirectory_ProviderDefault_WhenNoConfigOrMetadata()
    {
        var providerOptions = new CliProviderOptions { DefaultWorkingDirectory = "/from/provider" };

        var result = ExternalCliExecutionStrategy.ResolveWorkingDirectory(null, null, providerOptions);

        result.ShouldBe("/from/provider");
    }

    [Fact]
    public void ResolveWorkingDirectory_Throws_WhenAllEmpty()
    {
        var providerOptions = new CliProviderOptions();

        Should.Throw<BusinessException>(() =>
            ExternalCliExecutionStrategy.ResolveWorkingDirectory(null, null, providerOptions));
    }

    // ── ResolveCliProviderName ──

    [Fact]
    public void ResolveCliProviderName_Config_TakesPriority()
    {
        var config = new ExternalCliExecutionConfigDto { Provider = "from-config" };
        var options = new CliOptions { DefaultProvider = "from-global" };

        var result = ExternalCliExecutionStrategy.ResolveCliProviderName(config, options);

        result.ShouldBe("from-config");
    }

    [Fact]
    public void ResolveCliProviderName_GlobalDefault_WhenNoConfig()
    {
        var options = new CliOptions { DefaultProvider = "from-global" };

        var result = ExternalCliExecutionStrategy.ResolveCliProviderName(null, options);

        result.ShouldBe("from-global");
    }

    [Fact]
    public void ResolveCliProviderName_Throws_WhenBothEmpty()
    {
        var options = new CliOptions();

        Should.Throw<BusinessException>(() =>
            ExternalCliExecutionStrategy.ResolveCliProviderName(null, options));
    }
}
