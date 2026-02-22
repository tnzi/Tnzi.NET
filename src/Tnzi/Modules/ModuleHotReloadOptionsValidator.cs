
namespace Tnzi.Modules;

/// <summary>
/// 模块热重载配置选项验证器
/// </summary>
public class ModuleHotReloadOptionsValidator : OptionsValidatorBase<ModuleHotReloadOptions>
{
    /// <inheritdoc />
    protected override void ValidateOptions(ModuleHotReloadOptions options, List<string> errors)
    {
        if (options.ReloadDelayMs < 0)
        {
            errors.Add("ReloadDelayMs must be non-negative.");
        }

        if (options.RetryCount < 0)
        {
            errors.Add("RetryCount must be non-negative.");
        }

        if (options.RetryDelayMs < 0)
        {
            errors.Add("RetryDelayMs must be non-negative.");
        }

        if (options.WatchedFileExtensions == null || options.WatchedFileExtensions.Length == 0)
        {
            errors.Add("WatchedFileExtensions must not be null or empty.");
        }
    }
}