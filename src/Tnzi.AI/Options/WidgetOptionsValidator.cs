namespace Tnzi.AI.Options;

/// <summary>Validates <see cref="WidgetOptions"/>.</summary>
public class WidgetOptionsValidator : OptionsValidatorBase<WidgetOptions>
{
    protected override void ValidateOptions(WidgetOptions options, List<string> errors)
    {
        if (!options.Enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(options.RunnerPath) || !options.RunnerPath.StartsWith('/') || options.RunnerPath.StartsWith("//", StringComparison.Ordinal))
        {
            errors.Add("AI:Widgets:RunnerPath must be a path starting with a single '/'");
        }

        if (string.IsNullOrWhiteSpace(options.FrameAncestors) || options.FrameAncestors.IndexOfAny([';', ',', '\r', '\n']) >= 0)
        {
            errors.Add("AI:Widgets:FrameAncestors must be a CSP source list (for example \"*\" or \"https://chat.example.com\"), without ';' or ','");
        }
    }
}
