namespace Tnzi.AI.Sandbox.Options;

public class SandboxModuleOptionsValidator : OptionsValidatorBase<SandboxModuleOptions>
{
    private static readonly HashSet<string> SupportedProviders =
        new(StringComparer.OrdinalIgnoreCase) { "local", "docker", "kubernetes" };

    protected override void ValidateOptions(SandboxModuleOptions options, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(options.Provider))
            errors.Add("Provider must not be empty");
        else if (!SupportedProviders.Contains(options.Provider))
            errors.Add($"Provider '{options.Provider}' is not supported. Valid values: local, docker, kubernetes.");

        if (string.IsNullOrWhiteSpace(options.DataRoot))
            errors.Add("DataRoot must not be empty");
    }
}
