namespace Tnzi.AI.Cli.Options;

/// <summary>
/// CLI 配置验证器
/// </summary>
public class CliOptionsValidator : OptionsValidatorBase<CliOptions>
{
    protected override void ValidateOptions(CliOptions options, List<string> errors)
    {
        Check.NotNull(options);

        if (!string.IsNullOrEmpty(options.DefaultProvider)
            && !options.Providers.ContainsKey(options.DefaultProvider))
        {
            AddError(errors, nameof(options.DefaultProvider),
                $"Default provider '{options.DefaultProvider}' not found in Providers configuration.");
        }

        if (options.TimeoutSeconds <= 0 || options.TimeoutSeconds > 3600)
        {
            AddError(errors, nameof(options.TimeoutSeconds),
                $"TimeoutSeconds must be between 1 and 3600, got {options.TimeoutSeconds}.");
        }

        foreach (var (name, provider) in options.Providers)
        {
            if (string.IsNullOrWhiteSpace(provider.Command))
            {
                AddError(errors, $"Providers:{name}:Command",
                    $"Command is required for CLI provider '{name}'.");
            }
        }
    }

    protected override void CollectWarnings(CliOptions options, List<string> warnings)
    {
        Check.NotNull(options);

        foreach (var (name, provider) in options.Providers)
        {
            if (provider.EnvironmentWhitelist is null)
            {
                AddWarning(warnings, $"Providers:{name}:EnvironmentWhitelist",
                    $"CLI provider '{name}' has no EnvironmentWhitelist — the child process will inherit " +
                    "ALL host environment variables, including secrets (database connection strings, " +
                    "cloud credentials, internal tokens). Configure a whitelist (e.g. [\"PATH\", \"HOME\", " +
                    "\"USERPROFILE\", \"LANG\", \"TMP\", \"TEMP\", \"SYSTEMROOT\"]) for production deployments.");
            }
        }
    }
}
