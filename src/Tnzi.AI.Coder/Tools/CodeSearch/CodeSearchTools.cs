namespace Tnzi.AI.Coder.CodeSearch;

/// <summary>
/// 代码搜索工具组 — 使用正则表达式搜索代码
/// </summary>
[AIToolGroup("codesearch", "Code Search", "Search code files using regex")]
public partial class CodeSearchTools : IAIToolProvider
{
    private readonly IPathValidator _pathValidator;
    private readonly CoderOptions _options;
    private readonly ILogger<CodeSearchTools> _logger;

    public CodeSearchTools(IPathValidator pathValidator, IOptions<CoderOptions> options, ILogger<CodeSearchTools> logger)
    {
        _pathValidator = Check.NotNull(pathValidator);
        _options = Check.NotNull(options).Value;
        _logger = Check.NotNull(logger);
    }

    /// <summary>
    /// 使用正则搜索文件内容
    /// </summary>
    [AIFunction("grep", "Search file contents using regex", IsReadOnly = true, IsConcurrencySafe = true, Aliases = "rg,search", SearchHint = "grep search regex content")]
    public async Task<object> GrepAsync(
        [AIParameter("pattern", "Regex pattern")] string pattern,
        [AIParameter("path", "Search directory", false)] string? path = null,
        [AIParameter("glob", "File glob filter (e.g. *.cs, *.ts)", false)] string? glob = null,
        [AIParameter("context_lines", "Number of context lines before and after match", false)] int? contextLines = null,
        [AIParameter("max_results", "Maximum number of results", false)] int? maxResults = null,
        [AIParameter("respect_gitignore", "Respect .gitignore rules (default true)", false)] bool respectGitignore = true)
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

            // 编译正则
            Regex regex;
            try
            {
                regex = new Regex(pattern, RegexOptions.Compiled | RegexOptions.Multiline, TimeSpan.FromSeconds(5));
            }
            catch (RegexParseException ex)
            {
                return new { error = $"Invalid regex pattern: {ex.Message}" };
            }

            var maxResultCount = Math.Min(maxResults ?? 200, 1000);
            var contextLineCount = Math.Min(contextLines ?? 0, 10);
            var results = new List<object>();
            var filesSearched = 0;

            // 加载 .gitignore 规则
            IgnorePatternMatcher? ignoreMatcher = null;
            if (respectGitignore)
            {
                ignoreMatcher = IgnorePatternMatcher.LoadFromDirectory(resolvedDir, _options.ProjectRoot);
            }

            // 枚举文件
            var files = EnumerateSearchFiles(resolvedDir, glob);

            foreach (var filePath in files)
            {
                if (results.Count >= maxResultCount)
                {
                    break;
                }

                try
                {
                    // 按 .gitignore 规则过滤
                    if (ignoreMatcher != null)
                    {
                        var relativePath = Path.GetRelativePath(resolvedDir, filePath);
                        if (ignoreMatcher.IsIgnored(relativePath))
                        {
                            continue;
                        }
                    }

                    var fileInfo = new FileInfo(filePath);
                    // 跳过大文件和二进制文件
                    if (fileInfo.Length > _options.Sandbox.MaxFileReadSize || IsBinaryFile(filePath))
                    {
                        continue;
                    }

                    var lines = await File.ReadAllLinesAsync(filePath);
                    filesSearched++;

                    for (var i = 0; i < lines.Length && results.Count < maxResultCount; i++)
                    {
                        if (!regex.IsMatch(lines[i]))
                        {
                            continue;
                        }

                        var relativePath = Path.GetRelativePath(resolvedDir, filePath).Replace('\\', '/');
                        var lineNum = i + 1;

                        if (contextLineCount > 0)
                        {
                            // 带上下文的匹配
                            var startLine = Math.Max(0, i - contextLineCount);
                            var endLine = Math.Min(lines.Length - 1, i + contextLineCount);

                            var contextContent = new StringBuilder();
                            for (var j = startLine; j <= endLine; j++)
                            {
                                var prefix = j == i ? ">" : " ";
                                contextContent.AppendLine($"{prefix}{j + 1,6}\t{lines[j]}");
                            }

                            results.Add(new
                            {
                                file = relativePath,
                                line = lineNum,
                                content = lines[i],
                                context = contextContent.ToString().TrimEnd()
                            });
                        }
                        else
                        {
                            results.Add(new
                            {
                                file = relativePath,
                                line = lineNum,
                                content = lines[i]
                            });
                        }
                    }
                }
                catch (Exception)
                {
                    // 跳过无法读取的文件
                }
            }

            _logger.LogDebug("Grep '{Pattern}' in '{Path}': {Count} matches in {Files} files",
                pattern, searchDir, results.Count, filesSearched);

            return new
            {
                matches = results,
                count = results.Count,
                files_searched = filesSearched,
                truncated = results.Count >= maxResultCount
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Grep failed for pattern '{Pattern}'", pattern);
            return new { error = $"Grep failed: {ex.Message}" };
        }
    }

    /// <summary>
    /// 枚举搜索目标文件
    /// </summary>
    private static IEnumerable<string> EnumerateSearchFiles(string rootDir, string? glob)
    {
        var filePattern = "*";
        if (!string.IsNullOrEmpty(glob))
        {
            // 提取文件模式部分
            filePattern = glob.Contains('/') ? glob.Split('/').Last() : glob.Replace("**/", "");
            if (string.IsNullOrEmpty(filePattern)) filePattern = "*";
        }

        try
        {
            return Directory.EnumerateFiles(rootDir, filePattern, new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                MatchCasing = MatchCasing.CaseInsensitive
            });
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// 生成仓库代码大纲 — 提取类/接口/方法签名的层次化结构图
    /// </summary>
    [AIFunction("repo_outline", "Generate a hierarchical outline of classes, interfaces, and methods in a directory — useful for understanding codebase structure", IsReadOnly = true, IsConcurrencySafe = true, SearchHint = "outline structure classes methods")]
    public async Task<object> RepoOutlineAsync(
        [AIParameter("path", "Directory to outline", false)] string? path = null,
        [AIParameter("glob", "File glob filter (e.g. *.cs, *.ts)", false)] string? glob = null,
        [AIParameter("max_files", "Maximum number of files to process (default 100)", false)] int maxFiles = 100,
        [AIParameter("respect_gitignore", "Respect .gitignore rules (default true)", false)] bool respectGitignore = true)
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

            maxFiles = Math.Clamp(maxFiles, 1, 500);
            var effectiveGlob = glob ?? "*.cs";

            // 加载 .gitignore 规则
            IgnorePatternMatcher? ignoreMatcher = null;
            if (respectGitignore)
            {
                ignoreMatcher = IgnorePatternMatcher.LoadFromDirectory(resolvedDir, _options.ProjectRoot);
            }

            var files = EnumerateSearchFiles(resolvedDir, effectiveGlob);
            var outline = new List<object>();
            var processedFiles = 0;

            foreach (var filePath in files)
            {
                if (processedFiles >= maxFiles) break;

                try
                {
                    if (ignoreMatcher != null)
                    {
                        var relativePath = Path.GetRelativePath(resolvedDir, filePath);
                        if (ignoreMatcher.IsIgnored(relativePath)) continue;
                    }

                    var fileInfo = new FileInfo(filePath);
                    if (fileInfo.Length > _options.Sandbox.MaxFileReadSize || IsBinaryFile(filePath))
                        continue;

                    var content = await File.ReadAllTextAsync(filePath);
                    processedFiles++;

                    var ext = Path.GetExtension(filePath).ToLower();
                    var symbols = ext switch
                    {
                        ".cs" => ExtractCSharpSymbols(content),
                        ".ts" or ".tsx" => ExtractTypeScriptSymbols(content),
                        ".js" or ".jsx" => ExtractTypeScriptSymbols(content),
                        ".vue" => ExtractVueSymbols(content),
                        _ => ExtractGenericSymbols(content)
                    };

                    if (symbols.Count > 0)
                    {
                        outline.Add(new
                        {
                            file = Path.GetRelativePath(resolvedDir, filePath).Replace('\\', '/'),
                            symbols
                        });
                    }
                }
                catch
                {
                    // 跳过无法读取的文件
                }
            }

            _logger.LogDebug("Repo outline in '{Path}': {Files} files, {Symbols} symbol entries",
                searchDir, processedFiles, outline.Count);

            return new
            {
                outline,
                files_processed = processedFiles,
                root = searchDir
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Repo outline failed");
            return new { error = $"Repo outline failed: {ex.Message}" };
        }
    }

    /// <summary>
    /// 提取 C# 文件的类/接口/方法签名
    /// </summary>
    private static List<object> ExtractCSharpSymbols(string content)
    {
        var symbols = new List<object>();

        // 匹配类、接口、结构、枚举、record 声明
        var typePattern = CSharpTypeRegex();
        foreach (Match m in typePattern.Matches(content))
        {
            symbols.Add(new { kind = m.Groups[1].Value, name = m.Groups[2].Value.Trim() });
        }

        // 匹配方法签名（public/protected/internal/private + 返回类型 + 方法名 + 参数列表）
        var methodPattern = CSharpMethodRegex();
        foreach (Match m in methodPattern.Matches(content))
        {
            var access = m.Groups[1].Value;
            var returnType = m.Groups[2].Value.Trim();
            var methodName = m.Groups[3].Value;
            symbols.Add(new { kind = "method", name = $"{access} {returnType} {methodName}()" });
        }

        return symbols;
    }

    /// <summary>
    /// 提取 TypeScript/JavaScript 文件的符号
    /// </summary>
    private static List<object> ExtractTypeScriptSymbols(string content)
    {
        var symbols = new List<object>();

        // 匹配 export class/interface/type/enum/function
        var exportPattern = TypeScriptExportRegex();
        foreach (Match m in exportPattern.Matches(content))
        {
            symbols.Add(new { kind = m.Groups[1].Value, name = m.Groups[2].Value });
        }

        // 匹配 export const/let + 箭头函数
        var constFnPattern = TypeScriptConstFnRegex();
        foreach (Match m in constFnPattern.Matches(content))
        {
            symbols.Add(new { kind = "const", name = m.Groups[1].Value });
        }

        return symbols;
    }

    /// <summary>
    /// 提取 Vue SFC 的 script 部分符号
    /// </summary>
    private static List<object> ExtractVueSymbols(string content)
    {
        var symbols = new List<object>();

        // 提取 defineProps/defineEmits/defineModel
        var definePattern = VueDefineRegex();
        foreach (Match m in definePattern.Matches(content))
        {
            symbols.Add(new { kind = "macro", name = m.Groups[1].Value });
        }

        // 提取组件名（从 <script> 的 name 或文件名推断）
        var scriptSymbols = ExtractTypeScriptSymbols(content);
        symbols.AddRange(scriptSymbols);

        return symbols;
    }

    /// <summary>
    /// 通用文件符号提取（按函数/类名模式）
    /// </summary>
    private static List<object> ExtractGenericSymbols(string content)
    {
        var symbols = new List<object>();
        var funcPattern = GenericFunctionRegex();
        foreach (Match m in funcPattern.Matches(content))
        {
            symbols.Add(new { kind = "function", name = m.Groups[1].Value });
        }
        return symbols;
    }

    // 预编译正则（带超时保护）
    [GeneratedRegex(@"^\s*(?:public\s+|internal\s+|protected\s+|private\s+)?(?:abstract\s+|sealed\s+|static\s+|partial\s+)*(class|interface|struct|enum|record)\s+(\w+)", RegexOptions.Multiline, matchTimeoutMilliseconds: 5000)]
    private static partial Regex CSharpTypeRegex();

    // 第一个捕获组匹配访问修饰符, 第二个匹配返回类型, 第三个匹配方法名
    [GeneratedRegex(@"^\s*(public|protected|internal|private)\s+(?:static\s+|virtual\s+|override\s+|abstract\s+|async\s+)*(\S+)\s+(\w+)\s*\(", RegexOptions.Multiline, matchTimeoutMilliseconds: 5000)]
    private static partial Regex CSharpMethodRegex();

    [GeneratedRegex(@"export\s+(?:default\s+)?(?:abstract\s+)?(class|interface|type|enum|function)\s+(\w+)", RegexOptions.Multiline, matchTimeoutMilliseconds: 5000)]
    private static partial Regex TypeScriptExportRegex();

    [GeneratedRegex(@"export\s+(?:const|let)\s+(\w+)\s*=", RegexOptions.Multiline, matchTimeoutMilliseconds: 5000)]
    private static partial Regex TypeScriptConstFnRegex();

    [GeneratedRegex(@"(defineProps|defineEmits|defineModel|defineExpose|defineSlots)\s*[<(]", RegexOptions.Multiline, matchTimeoutMilliseconds: 5000)]
    private static partial Regex VueDefineRegex();

    [GeneratedRegex(@"(?:function|def|fn)\s+(\w+)\s*\(", RegexOptions.Multiline, matchTimeoutMilliseconds: 5000)]
    private static partial Regex GenericFunctionRegex();

    /// <summary>
    /// 检测是否为二进制文件或位于默认忽略目录中
    /// </summary>
    private static bool IsBinaryFile(string filePath)
    {
        // 检查文件是否位于默认忽略目录中
        var normalizedPath = filePath.Replace('\\', '/');
        var defaultIgnored = IgnorePatternMatcher.GetDefaultIgnoredDirectories();
        foreach (var dir in defaultIgnored)
        {
            if (normalizedPath.Contains($"/{dir}/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        var extension = Path.GetExtension(filePath).ToLower();
        return extension is ".exe" or ".dll" or ".obj" or ".bin" or ".zip" or ".tar"
            or ".gz" or ".rar" or ".7z" or ".png" or ".jpg" or ".jpeg" or ".gif"
            or ".bmp" or ".ico" or ".pdf" or ".woff" or ".woff2" or ".ttf"
            or ".eot" or ".mp3" or ".mp4" or ".avi" or ".mov" or ".pdb"
            or ".nupkg" or ".snupkg";
    }
}
