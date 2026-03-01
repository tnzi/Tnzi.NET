namespace Tnzi.Feature.Options;

/// <summary>
/// Feature module options validator
/// </summary>
public class FeatureOptionsValidator : OptionsValidatorBase<FeatureOptions>
{
    /// <inheritdoc />
    protected override void ValidateOptions(FeatureOptions options, List<string> errors)
    {
        if (options.CacheRefreshIntervalMinutes < 0)
        {
            AddError(errors, nameof(FeatureOptions.CacheRefreshIntervalMinutes),
                "must be greater than or equal to 0.");
        }
    }
}
