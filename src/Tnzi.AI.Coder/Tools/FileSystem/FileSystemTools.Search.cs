namespace Tnzi.AI.Coder.FileSystem;

/// <summary>
/// 文件系统工具组 — 搜索、列表、复制、移动方法
/// </summary>
public partial class FileSystemTools
{
    /// <summary>
    /// 查找匹配 glob 模式的文件
    /// </summary>
    [AIFunction("glob", "Find files matching a glob pattern",
        IsReadOnly = true, IsConcurrencySafe = true, Aliases = "find,search", SearchHint = "glob find files pattern")]
    public async Task<object> GlobAsync(
        [AIParameter("pattern", "Glob pattern (e.g. **/*.cs)")] string pattern,
        [AIParameter("path", "Search directory", false)] string? path = null,
        [AIParameter("respect_gitignore", "Exclude files matching .gitignore patterns", false)] bool respectGitignore = true)
    {
        try
        {
            var searchDir = path ?? _options.ProjectRoot;
            var validation = await _pathValidator.ValidateAsync(searchDir);
            if (!validation.IsValid)
            {
                return new { error = validation.Error };
            }

            var resolvedDir = validation.ResolvedPath!;

            if (!Directory.Exists(resolvedDir))
            {
                return new { error = $"Directory not found: {searchDir}" };
            }

            // 加载 .gitignore 规则
            var gitignoreRules = respectGitignore
                ? LoadGitignoreRules(resolvedDir)
                : [];

            // 简单 glob 实现：将 glob 转换为 searchPattern
            var files = EnumerateFilesWithGlob(resolvedDir, pattern)
                .Where(f => !IsGitignored(f, resolvedDir, gitignoreRules))
                .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
                .Take(1000) // 限制返回数量
                .Select(f => Path.GetRelativePath(resolvedDir, f).Replace('\\', '/'))
                .ToList();

            _logger.LogDebug("Glob '{Pattern}' in '{Path}': found {Count} files (gitignore={Gitignore})",
                pattern, searchDir, files.Count, respectGitignore);

            return new
            {
                files,
                count = files.Count,
                truncated = files.Count >= 1000
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Glob failed for pattern '{Pattern}'", pattern);
            return new { error = $"Glob failed: {ex.Message}" };
        }
    }

    /// <summary>
    /// 列出目录内容
    /// </summary>
    [AIFunction("list_directory", "List files and directories",
        IsReadOnly = true, IsConcurrencySafe = true, Aliases = "ls,dir", SearchHint = "list directory files")]
    public async Task<object> ListDirectoryAsync(
        [AIParameter("path", "Directory path")] string path)
    {
        try
        {
            var validation = await _pathValidator.ValidateAsync(path);
            if (!validation.IsValid)
            {
                return new { error = validation.Error };
            }

            var resolvedPath = validation.ResolvedPath!;

            if (!Directory.Exists(resolvedPath))
            {
                return new { error = $"Directory not found: {path}" };
            }

            var entries = new List<object>();

            foreach (var dir in Directory.GetDirectories(resolvedPath).OrderBy(d => d))
            {
                entries.Add(new
                {
                    name = Path.GetFileName(dir),
                    type = "directory"
                });
            }

            foreach (var file in Directory.GetFiles(resolvedPath).OrderBy(f => f))
            {
                var info = new FileInfo(file);
                entries.Add(new
                {
                    name = Path.GetFileName(file),
                    type = "file",
                    size = info.Length,
                    modified = info.LastWriteTimeUtc.ToString("yyyy-MM-dd HH:mm:ss")
                });
            }

            _logger.LogDebug("Listed directory '{Path}': {Count} entries", path, entries.Count);

            return new
            {
                path = resolvedPath,
                entries,
                count = entries.Count
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to list directory '{Path}'", path);
            return new { error = $"Failed to list directory: {ex.Message}" };
        }
    }

    /// <summary>
    /// 复制文件
    /// </summary>
    [AIFunction("copy_file", "Copy a file from source to destination",
        SearchHint = "copy duplicate file")]
    public async Task<object> CopyFileAsync(
        [AIParameter("source", "Source file path")] string source,
        [AIParameter("destination", "Destination file path")] string destination,
        [AIParameter("overwrite", "Overwrite if destination exists", false)] bool overwrite = false)
    {
        try
        {
            var srcValidation = await _pathValidator.ValidateAsync(source);
            if (!srcValidation.IsValid)
            {
                return new { error = $"Source: {srcValidation.Error}" };
            }

            var destValidation = await _pathValidator.ValidateAsync(destination);
            if (!destValidation.IsValid)
            {
                return new { error = $"Destination: {destValidation.Error}" };
            }

            var resolvedSrc = srcValidation.ResolvedPath!;
            var resolvedDest = destValidation.ResolvedPath!;

            if (!File.Exists(resolvedSrc))
            {
                return new { error = $"Source file not found: {source}" };
            }

            if (!overwrite && File.Exists(resolvedDest))
            {
                return new { error = $"Destination file already exists: {destination}. Use overwrite=true to replace." };
            }

            // 确保目标目录存在
            var destDir = Path.GetDirectoryName(resolvedDest);
            if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            File.Copy(resolvedSrc, resolvedDest, overwrite);

            _logger.LogDebug("Copied '{Source}' to '{Destination}'", source, destination);

            return new
            {
                success = true,
                source = resolvedSrc,
                destination = resolvedDest
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to copy file '{Source}' to '{Destination}'", source, destination);
            return new { error = $"Failed to copy file: {ex.Message}" };
        }
    }

    /// <summary>
    /// 移动/重命名文件
    /// </summary>
    [AIFunction("move_file", "Move or rename a file",
        SearchHint = "move rename file")]
    public async Task<object> MoveFileAsync(
        [AIParameter("source", "Source file path")] string source,
        [AIParameter("destination", "Destination file path")] string destination,
        [AIParameter("overwrite", "Overwrite if destination exists", false)] bool overwrite = false)
    {
        try
        {
            var srcValidation = await _pathValidator.ValidateAsync(source);
            if (!srcValidation.IsValid)
            {
                return new { error = $"Source: {srcValidation.Error}" };
            }

            var destValidation = await _pathValidator.ValidateAsync(destination);
            if (!destValidation.IsValid)
            {
                return new { error = $"Destination: {destValidation.Error}" };
            }

            var resolvedSrc = srcValidation.ResolvedPath!;
            var resolvedDest = destValidation.ResolvedPath!;

            if (!File.Exists(resolvedSrc))
            {
                return new { error = $"Source file not found: {source}" };
            }

            if (!overwrite && File.Exists(resolvedDest))
            {
                return new { error = $"Destination file already exists: {destination}. Use overwrite=true to replace." };
            }

            // 确保目标目录存在
            var destDir = Path.GetDirectoryName(resolvedDest);
            if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            File.Move(resolvedSrc, resolvedDest, overwrite);

            _logger.LogDebug("Moved '{Source}' to '{Destination}'", source, destination);

            return new
            {
                success = true,
                source = resolvedSrc,
                destination = resolvedDest
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to move file '{Source}' to '{Destination}'", source, destination);
            return new { error = $"Failed to move file: {ex.Message}" };
        }
    }

    /// <summary>
    /// 使用简单 glob 模式枚举文件
    /// </summary>
    private static IEnumerable<string> EnumerateFilesWithGlob(string rootDir, string pattern)
    {
        // 处理 ** 递归模式
        var searchOption = pattern.Contains("**")
            ? SearchOption.AllDirectories
            : SearchOption.TopDirectoryOnly;

        // 提取文件搜索模式（去除目录部分）
        var filePattern = pattern;
        if (pattern.Contains('/'))
        {
            filePattern = pattern.Split('/').Last();
        }
        else if (pattern.Contains("**/"))
        {
            filePattern = pattern.Replace("**/", "");
        }
        else if (pattern.StartsWith("**"))
        {
            filePattern = pattern[2..].TrimStart('/');
        }

        // 如果模式为空，匹配所有文件
        if (string.IsNullOrEmpty(filePattern))
        {
            filePattern = "*";
        }

        try
        {
            return Directory.EnumerateFiles(rootDir, filePattern, new EnumerationOptions
            {
                RecurseSubdirectories = searchOption == SearchOption.AllDirectories,
                IgnoreInaccessible = true,
                MatchCasing = MatchCasing.CaseInsensitive
            });
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>
    /// 从搜索目录向上加载所有 .gitignore 规则（直到 ProjectRoot）
    /// </summary>
    private List<GitignoreRule> LoadGitignoreRules(string searchDir)
    {
        var rules = new List<GitignoreRule>();
        var projectRoot = Path.GetFullPath(_options.ProjectRoot).Replace('\\', '/');
        var currentDir = searchDir.Replace('\\', '/');

        // 从搜索目录往上遍历，直到 ProjectRoot
        while (true)
        {
            var gitignorePath = Path.Combine(currentDir.Replace('/', Path.DirectorySeparatorChar), ".gitignore");
            if (File.Exists(gitignorePath))
            {
                try
                {
                    var lines = File.ReadAllLines(gitignorePath);
                    foreach (var line in lines)
                    {
                        var trimmed = line.Trim();

                        // 跳过空行和注释
                        if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
                        {
                            continue;
                        }

                        var isNegation = trimmed.StartsWith('!');
                        var pattern = isNegation ? trimmed[1..] : trimmed;

                        rules.Add(new GitignoreRule(pattern, isNegation, currentDir));
                    }
                }
                catch
                {
                    // 忽略读取失败
                }
            }

            // 已到达 ProjectRoot 或更上层，停止
            if (currentDir.Equals(projectRoot, StringComparison.OrdinalIgnoreCase)
                || currentDir.Length <= projectRoot.Length)
            {
                break;
            }

            var parent = Path.GetDirectoryName(currentDir.Replace('/', Path.DirectorySeparatorChar));
            if (string.IsNullOrEmpty(parent))
            {
                break;
            }
            currentDir = parent.Replace('\\', '/');
        }

        return rules;
    }

    /// <summary>
    /// 检查文件是否匹配 .gitignore 规则
    /// </summary>
    private static bool IsGitignored(string filePath, string rootDir, List<GitignoreRule> rules)
    {
        if (rules.Count == 0) return false;

        var normalizedPath = filePath.Replace('\\', '/');
        var relativePath = Path.GetRelativePath(rootDir, filePath).Replace('\\', '/');
        var fileName = Path.GetFileName(filePath);

        var ignored = false;

        foreach (var rule in rules)
        {
            var matches = MatchesGitignorePattern(relativePath, fileName, normalizedPath, rule);
            if (matches)
            {
                ignored = !rule.IsNegation;
            }
        }

        return ignored;
    }

    /// <summary>
    /// 检查路径是否匹配单个 .gitignore 模式
    /// </summary>
    private static bool MatchesGitignorePattern(string relativePath, string fileName, string fullPath, GitignoreRule rule)
    {
        var pattern = rule.Pattern.TrimEnd('/');
        var isDirectoryPattern = rule.Pattern.EndsWith('/');

        // 如果是目录模式，检查路径中是否包含该目录
        if (isDirectoryPattern)
        {
            return relativePath.StartsWith(pattern + "/", StringComparison.OrdinalIgnoreCase)
                || relativePath.Contains("/" + pattern + "/", StringComparison.OrdinalIgnoreCase);
        }

        // 扩展名匹配: *.log
        if (pattern.StartsWith('*') && !pattern.Contains('/'))
        {
            var extension = pattern[1..];
            return fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase);
        }

        // 没有路径分隔符的模式：匹配文件名或路径中的目录名
        if (!pattern.Contains('/'))
        {
            // 精确匹配文件名
            if (fileName.Equals(pattern, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            // 匹配路径中的目录名
            return relativePath.StartsWith(pattern + "/", StringComparison.OrdinalIgnoreCase)
                || relativePath.Contains("/" + pattern + "/", StringComparison.OrdinalIgnoreCase);
        }

        // 路径模式：相对于 .gitignore 所在目录的匹配
        var ruleRelativePath = Path.GetRelativePath(
            rule.BaseDirectory.Replace('/', Path.DirectorySeparatorChar),
            fullPath.Replace('/', Path.DirectorySeparatorChar)).Replace('\\', '/');

        return SimpleWildcardMatch(ruleRelativePath, pattern);
    }

    /// <summary>
    /// 简单通配符匹配（支持 * 和 **）
    /// </summary>
    private static bool SimpleWildcardMatch(string path, string pattern)
    {
        // 将 pattern 转换为正则
        var regexPattern = "^"
            + Regex.Escape(pattern)
                .Replace("\\*\\*", ".*")     // ** 匹配任意路径
                .Replace("\\*", "[^/]*")     // * 匹配非路径分隔符
                .Replace("\\?", "[^/]")      // ? 匹配单个字符
            + "$";

        return Regex.IsMatch(path, regexPattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// .gitignore 规则
    /// </summary>
    private readonly record struct GitignoreRule(string Pattern, bool IsNegation, string BaseDirectory);
}
