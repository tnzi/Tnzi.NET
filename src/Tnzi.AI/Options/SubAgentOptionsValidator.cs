namespace Tnzi.AI.Options;

public class SubAgentOptionsValidator : OptionsValidatorBase<SubAgentOptions>
{
    protected override void ValidateOptions(SubAgentOptions options, List<string> errors)
    {
        if (options.MaxConcurrentSubAgents is < 2 or > 4)
            errors.Add("MaxConcurrentSubAgents must be between 2 and 4");

        if (options.TimeoutSeconds < 1)
            errors.Add("TimeoutSeconds must be >= 1");

        if (options.MaxDepth < 1)
            errors.Add("MaxDepth must be >= 1");

        if (options.MaxDescendantsPerRoot < 1)
            errors.Add("MaxDescendantsPerRoot must be >= 1");
    }
}
