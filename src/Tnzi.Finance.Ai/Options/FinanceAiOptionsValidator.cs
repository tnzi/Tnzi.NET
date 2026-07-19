namespace Tnzi.Finance.Ai.Options;

/// <summary>
/// Validator for <see cref="FinanceAiOptions"/>.
/// </summary>
public class FinanceAiOptionsValidator : OptionsValidatorBase<FinanceAiOptions>
{
    protected override void ValidateOptions(FinanceAiOptions options, List<string> errors)
    {
        if (options.MaxFileSizeMb is < 1 or > 100)
            AddError(errors, nameof(FinanceAiOptions.MaxFileSizeMb), "must be between 1 and 100.", "1..100");
    }
}
