using Tnzi.Options;

namespace Tnzi.AI.Sandbox.Options;

public class SandboxModuleOptionsValidator : OptionsValidatorBase<SandboxModuleOptions>
{
    protected override void ValidateOptions(SandboxModuleOptions options, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(options.Provider))
            errors.Add("Provider must not be empty");

        if (string.IsNullOrWhiteSpace(options.DataRoot))
            errors.Add("DataRoot must not be empty");
    }
}
