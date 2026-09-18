namespace Tnzi.AI.Options;

public class SubAgentOptionsValidator : OptionsValidatorBase<SubAgentOptions>
{
    protected override void ValidateOptions(SubAgentOptions options, List<string> errors)
    {
        // 与 [RuntimeSetting(Min = 1, Max = 64)] 同口径：此前锁在 2..4，而且没有任何读者在用这个值
        if (options.MaxConcurrentSubAgents is < 1 or > 64)
            errors.Add("MaxConcurrentSubAgents must be between 1 and 64");

        if (options.TimeoutSeconds < 1)
            errors.Add("TimeoutSeconds must be >= 1");

        if (options.MaxDepth < 1)
            errors.Add("MaxDepth must be >= 1");

        if (options.MaxDescendantsPerRoot < 1)
            errors.Add("MaxDescendantsPerRoot must be >= 1");
    }
}
