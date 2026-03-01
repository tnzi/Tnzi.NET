namespace Tnzi.AI.Coder.FileSystem;

/// <summary>
/// 路径验证器实现 — 沙箱安全检查
/// </summary>
public class PathValidator : IPathValidator
{
    private readonly CoderOptions _options;
    private readonly ILogger<PathValidator> _logger;
    private readonly string _resolvedProjectRoot;

    public PathValidator(IOptions<CoderOptions> options, ILogger<PathValidator> logger)
    {
        _options = Check.NotNull(options).Value;
        _logger = Check.NotNull(logger);
        _resolvedProjectRoot = Path.GetFullPath(_options.ProjectRoot);
    }

    /// <inheritdoc />
    public Task<PathValidationResult> ValidateAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Task.FromResult(PathValidationResult.Failure("Path cannot be empty"));
        }

        try
        {
            // 解析为绝对路径
            var resolvedPath = Path.IsPathRooted(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(Path.Combine(_resolvedProjectRoot, path));

            // 规范化路径分隔符
            resolvedPath = resolvedPath.Replace('\\', '/');
            var normalizedRoot = _resolvedProjectRoot.Replace('\\', '/');

            // 检查是否在允许的目录列表中
            if (!IsInAllowedDirectory(resolvedPath, normalizedRoot))
            {
                _logger.LogDebug("Path '{Path}' is outside allowed directories", path);
                return Task.FromResult(PathValidationResult.Failure(
                    $"Path '{path}' is outside the allowed directories"));
            }

            // 检查拒绝模式
            if (MatchesDeniedPattern(resolvedPath))
            {
                _logger.LogDebug("Path '{Path}' matches denied pattern", path);
                return Task.FromResult(PathValidationResult.Failure(
                    $"Path '{path}' matches a denied pattern (sensitive file)"));
            }

            // 检查符号链接逃逸（文件和目录）
            // 使用 ResolveLinkTarget(returnFinalTarget: true) 解析整条链路
            // 注意: 验证和实际使用之间存在 TOCTOU 时间窗口（文件系统固有限制）
            if (File.Exists(resolvedPath))
            {
                var fileInfo = new FileInfo(resolvedPath);
                if (fileInfo.LinkTarget != null)
                {
                    // 解析整条符号链接链（A→B→C 检查最终目标 C）
                    var finalTarget = fileInfo.ResolveLinkTarget(returnFinalTarget: true);
                    var targetPath = finalTarget != null
                        ? Path.GetFullPath(finalTarget.FullName).Replace('\\', '/')
                        : Path.GetFullPath(fileInfo.LinkTarget).Replace('\\', '/');

                    if (!IsInAllowedDirectory(targetPath, normalizedRoot))
                    {
                        _logger.LogDebug("Symbolic link '{Path}' points outside allowed directories (target: {Target})", path, targetPath);
                        return Task.FromResult(PathValidationResult.Failure(
                            $"Symbolic link '{path}' points outside allowed directories"));
                    }
                }
            }
            else if (Directory.Exists(resolvedPath))
            {
                var dirInfo = new DirectoryInfo(resolvedPath);
                if (dirInfo.LinkTarget != null)
                {
                    var finalTarget = dirInfo.ResolveLinkTarget(returnFinalTarget: true);
                    var targetPath = finalTarget != null
                        ? Path.GetFullPath(finalTarget.FullName).Replace('\\', '/')
                        : Path.GetFullPath(dirInfo.LinkTarget).Replace('\\', '/');

                    if (!IsInAllowedDirectory(targetPath, normalizedRoot))
                    {
                        _logger.LogDebug("Symbolic link '{Path}' points outside allowed directories (target: {Target})", path, targetPath);
                        return Task.FromResult(PathValidationResult.Failure(
                            $"Symbolic link '{path}' points outside allowed directories"));
                    }
                }
            }

            return Task.FromResult(PathValidationResult.Success(resolvedPath));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Path validation failed for '{Path}'", path);
            return Task.FromResult(PathValidationResult.Failure($"Invalid path: {ex.Message}"));
        }
    }

    /// <summary>
    /// 检查路径是否在允许的目录中
    /// </summary>
    private bool IsInAllowedDirectory(string resolvedPath, string normalizedRoot)
    {
        foreach (var allowedDir in _options.Sandbox.AllowedDirectories)
        {
            var resolvedAllowed = allowedDir == "."
                ? normalizedRoot
                : Path.IsPathRooted(allowedDir)
                    ? Path.GetFullPath(allowedDir).Replace('\\', '/')
                    : Path.GetFullPath(Path.Combine(_resolvedProjectRoot, allowedDir)).Replace('\\', '/');

            // 确保以 / 结尾进行前缀匹配
            if (!resolvedAllowed.EndsWith('/'))
            {
                resolvedAllowed += '/';
            }

            if (resolvedPath.StartsWith(resolvedAllowed, StringComparison.OrdinalIgnoreCase)
                || resolvedPath.Equals(resolvedAllowed.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 检查路径是否匹配拒绝模式
    /// </summary>
    private bool MatchesDeniedPattern(string resolvedPath)
    {
        var fileName = Path.GetFileName(resolvedPath);

        foreach (var pattern in _options.Sandbox.DeniedPatterns)
        {
            // 去掉 **/ 前缀，取文件名模式
            var filePattern = pattern.Replace("**/", "");

            if (filePattern.StartsWith("*"))
            {
                // 扩展名匹配: *.key, *.pem
                var extension = filePattern[1..];
                if (fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            else if (filePattern.EndsWith("*"))
            {
                // 前缀匹配: credentials*
                var prefix = filePattern[..^1];
                if (fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            else
            {
                // 精确匹配（含通配符展开）: .env, .env.*
                if (filePattern.Contains(".*"))
                {
                    // .env.* 匹配 .env.local, .env.production 等
                    var basePattern = filePattern.Replace(".*", ".");
                    if (fileName.StartsWith(basePattern, StringComparison.OrdinalIgnoreCase)
                        || fileName.Equals(basePattern.TrimEnd('.'), StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                else if (fileName.Equals(filePattern, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
