namespace Tnzi.AI.Coder.Options;

/// <summary>
/// AI Coder 配置选项验证器
/// </summary>
public class CoderOptionsValidator : OptionsValidatorBase<CoderOptions>
{
    protected override void ValidateOptions(CoderOptions options, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(options.ProjectRoot))
        {
            errors.Add("ProjectRoot cannot be null or empty");
        }

        if (options.MaxToolIterations < 1 || options.MaxToolIterations > 100)
        {
            errors.Add("MaxToolIterations must be between 1 and 100");
        }

        if (string.IsNullOrWhiteSpace(options.MemoryDirectory))
        {
            errors.Add("MemoryDirectory cannot be null or empty");
        }

        if (options.InstructionFiles == null || options.InstructionFiles.Count == 0)
        {
            errors.Add("InstructionFiles must contain at least one file path");
        }
        else
        {
            foreach (var file in options.InstructionFiles)
            {
                if (string.IsNullOrWhiteSpace(file))
                {
                    errors.Add("InstructionFiles must not contain empty entries");
                    break;
                }
            }
        }

        // 验证沙箱选项
        ValidateSandboxOptions(options.Sandbox, errors);
    }

    private static void ValidateSandboxOptions(SandboxOptions sandbox, List<string> errors)
    {
        if (sandbox.DefaultCommandTimeoutMs < 1000 || sandbox.DefaultCommandTimeoutMs > 600_000)
        {
            errors.Add("Sandbox.DefaultCommandTimeoutMs must be between 1000 and 600000");
        }

        if (sandbox.MaxFileReadSize < 1024 || sandbox.MaxFileReadSize > 104_857_600)
        {
            errors.Add("Sandbox.MaxFileReadSize must be between 1KB and 100MB");
        }

        if (sandbox.MaxOutputSize < 1024 || sandbox.MaxOutputSize > 10_485_760)
        {
            errors.Add("Sandbox.MaxOutputSize must be between 1KB and 10MB");
        }

        if (sandbox.MaxBackgroundProcesses < 1 || sandbox.MaxBackgroundProcesses > 50)
        {
            errors.Add("Sandbox.MaxBackgroundProcesses must be between 1 and 50");
        }
    }
}
