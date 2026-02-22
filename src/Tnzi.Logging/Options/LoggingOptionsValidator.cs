
namespace Tnzi.Logging.Options;

/// <summary>
/// 日志配置选项验证器
/// </summary>
public class LoggingOptionsValidator : OptionsValidatorBase<LoggingOptions>
{
    /// <summary>
    /// 验证日志配置选项
    /// </summary>
    protected override void ValidateOptions(LoggingOptions options, List<string> errors)
    {
        // 验证基础路径
        if (string.IsNullOrWhiteSpace(options.BasePath))
        {
            errors.Add("Logging.BasePath cannot be empty.");
        }

        // 验证文件输出配置
        if (options.FileOutput != null)
        {
            ValidateLevelOptions("Information", options.FileOutput.Information, errors);
            ValidateLevelOptions("Warning", options.FileOutput.Warning, errors);
            ValidateLevelOptions("Error", options.FileOutput.Error, errors);
            ValidateLevelOptions("Fatal", options.FileOutput.Fatal, errors);
            ValidateLevelOptions("Debug", options.FileOutput.Debug, errors);
        }
        else
        {
            errors.Add("Logging.FileOutput cannot be null.");
        }

        // 验证刷新间隔
        if (options.FlushToDiskInterval <= TimeSpan.Zero)
        {
            errors.Add("Logging.FlushToDiskInterval must be greater than zero.");
        }

        // 验证请求日志配置
        if (options.RequestLogging == null)
        {
            errors.Add("Logging.RequestLogging cannot be null.");
        }
    }

    private static void ValidateLevelOptions(string levelName, LevelFileOptions? options, List<string> errors)
    {
        if (options == null)
        {
            errors.Add($"Logging.FileOutput.{levelName} cannot be null.");
            return;
        }

        if (options.Enabled && options.RetainedFileCountLimit <= 0)
        {
            errors.Add($"Logging.FileOutput.{levelName}.RetainedFileCountLimit must be greater than 0 when Enabled is true.");
        }
    }
}