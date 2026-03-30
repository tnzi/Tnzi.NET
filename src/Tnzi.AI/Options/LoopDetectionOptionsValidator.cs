namespace Tnzi.AI.Options;

public class LoopDetectionOptionsValidator : OptionsValidatorBase<LoopDetectionOptions>
{
    protected override void ValidateOptions(LoopDetectionOptions options, List<string> errors)
    {
        if (options.WarnThreshold < 1)
            errors.Add("WarnThreshold must be >= 1");

        if (options.HardLimit <= options.WarnThreshold)
            errors.Add("HardLimit must be greater than WarnThreshold");

        if (options.WindowSize < options.HardLimit)
            errors.Add("WindowSize must be >= HardLimit");

        if (options.MaxTrackedThreads < 1)
            errors.Add("MaxTrackedThreads must be >= 1");
    }
}
