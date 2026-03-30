namespace Tnzi.AI.Options;

public class SuggestionOptionsValidator : OptionsValidatorBase<SuggestionOptions>
{
    protected override void ValidateOptions(SuggestionOptions options, List<string> errors)
    {
        if (options.Count < 1 || options.Count > 20)
            errors.Add("Count must be between 1 and 20");

        if (options.MaxWordsEn < 1)
            errors.Add("MaxWordsEn must be >= 1");

        if (options.MaxCharsCn < 1)
            errors.Add("MaxCharsCn must be >= 1");
    }
}
