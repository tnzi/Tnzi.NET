namespace Tnzi.AI.Options;

public class TodoOptionsValidator : OptionsValidatorBase<TodoOptions>
{
    protected override void ValidateOptions(TodoOptions options, List<string> errors)
    {
        if (options.MaxItems < 1 || options.MaxItems > 200)
            errors.Add("MaxItems must be between 1 and 200");
    }
}
