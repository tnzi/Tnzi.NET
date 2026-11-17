namespace Tnzi.AspNetCore.Options;

/// <summary>
/// <see cref="PostMigrationStartupTaskOptions"/> 配置验证器。
/// </summary>
public class PostMigrationStartupTaskOptionsValidator : OptionsValidatorBase<PostMigrationStartupTaskOptions>
{
    protected override void ValidateOptions(PostMigrationStartupTaskOptions options, List<string> errors)
    {
        if (options.RetryDelay <= TimeSpan.Zero)
        {
            errors.Add("RetryDelay must be positive.");
        }

        if (options.MaxRetryDelay < options.RetryDelay)
        {
            errors.Add("MaxRetryDelay must not be shorter than RetryDelay.");
        }
    }
}
