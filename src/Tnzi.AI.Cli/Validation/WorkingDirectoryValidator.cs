namespace Tnzi.AI.Cli.Validation;

/// <summary>
/// 工作目录安全白名单验证器
/// </summary>
public class WorkingDirectoryValidator
{
    private readonly CliOptions _options;

    public WorkingDirectoryValidator(IOptions<CliOptions> options)
    {
        _options = Check.NotNull(options).Value;
    }

    /// <summary>
    /// 验证工作目录是否在白名单内。空白名单 = 不限制。
    /// </summary>
    public void Validate(string workingDirectory)
    {
        Check.NotNullOrWhiteSpace(workingDirectory);

        if (_options.AllowedDirectories.Count == 0) return;

        var fullPath = Path.GetFullPath(workingDirectory);
        var allowed = _options.AllowedDirectories.Any(pattern => MatchDirectory(fullPath, pattern));

        if (!allowed)
        {
            throw new ForbiddenException(
                $"Working directory '{workingDirectory}' is not in the allowed directories list.");
        }
    }

    private static bool MatchDirectory(string path, string pattern)
    {
        var normalized = pattern.TrimEnd('*', '\\', '/');
        if (!normalized.EndsWith(Path.DirectorySeparatorChar) && !normalized.EndsWith(Path.AltDirectorySeparatorChar))
        {
            normalized += Path.DirectorySeparatorChar;
        }

        return path.StartsWith(normalized, StringComparison.OrdinalIgnoreCase)
               || path.Equals(normalized.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
    }
}
