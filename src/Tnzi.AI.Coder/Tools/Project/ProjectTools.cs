namespace Tnzi.AI.Coder.Project;

/// <summary>
/// 项目上下文工具组 — 加载项目指令和 Git 信息
/// </summary>
[AIToolGroup("project", "Project Context", "Load project instructions and git info")]
public class ProjectTools : IAIToolProvider
{
    private readonly IProjectContextLoader _contextLoader;
    private readonly IPathValidator _pathValidator;
    private readonly CoderOptions _options;
    private readonly ILogger<ProjectTools> _logger;

    public ProjectTools(
        IProjectContextLoader contextLoader,
        IPathValidator pathValidator,
        IOptions<CoderOptions> options,
        ILogger<ProjectTools> logger)
    {
        _contextLoader = Check.NotNull(contextLoader);
        _pathValidator = Check.NotNull(pathValidator);
        _options = Check.NotNull(options).Value;
        _logger = Check.NotNull(logger);
    }

    /// <summary>
    /// 加载项目指令文件（TNZI.md）
    /// </summary>
    [AIFunction("load_instructions", "Load TNZI.md project instructions")]
    public async Task<object> LoadInstructionsAsync(
        [AIParameter("project_root", "Project root directory", false)] string? projectRoot = null)
    {
        try
        {
            var context = await _contextLoader.LoadAsync(projectRoot);

            if (string.IsNullOrEmpty(context.Instructions))
            {
                return new
                {
                    instructions = (string?)null,
                    loaded_files = context.LoadedFiles,
                    project_root = context.ProjectRoot,
                    found = false
                };
            }

            _logger.LogDebug("Loaded project instructions from {Count} files", context.LoadedFiles.Count);

            return new
            {
                instructions = context.Instructions,
                loaded_files = context.LoadedFiles,
                project_root = context.ProjectRoot,
                metadata = context.Metadata,
                found = true,
                length = context.Instructions.Length
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to load project instructions");
            return new { error = $"Failed to load instructions: {ex.Message}" };
        }
    }

    /// <summary>
    /// 获取 Git 状态
    /// </summary>
    [AIFunction("git_status", "Get git status of the repository")]
    public async Task<object> GitStatusAsync(
        [AIParameter("path", "Repository path", false)] string? path = null)
    {
        try
        {
            var repoPath = path ?? _options.ProjectRoot;
            var validation = await _pathValidator.ValidateAsync(repoPath);
            if (!validation.IsValid)
            {
                return new { error = validation.Error };
            }

            var resolvedPath = validation.ResolvedPath!;
            var result = await RunGitCommandAsync(["status", "--porcelain", "-b"], resolvedPath);

            if (result.exitCode != 0)
            {
                return new { error = $"Git status failed: {result.stderr}" };
            }

            // 解析分支信息和文件状态
            var lines = result.stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var branch = "";
            var changes = new List<object>();

            foreach (var line in lines)
            {
                if (line.StartsWith("##"))
                {
                    branch = line[3..].Trim();
                }
                else if (line.Length >= 2)
                {
                    var status = line[..2].Trim();
                    var file = line[3..].Trim();
                    changes.Add(new { status, file });
                }
            }

            return new
            {
                branch,
                changes,
                clean = changes.Count == 0,
                raw = result.stdout
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to get git status");
            return new { error = $"Failed to get git status: {ex.Message}" };
        }
    }

    /// <summary>
    /// 获取 Git 提交日志
    /// </summary>
    [AIFunction("git_log", "Get recent git commits")]
    public async Task<object> GitLogAsync(
        [AIParameter("count", "Number of commits to show", false)] int count = 10,
        [AIParameter("path", "Repository path", false)] string? path = null)
    {
        try
        {
            var repoPath = path ?? _options.ProjectRoot;
            var validation = await _pathValidator.ValidateAsync(repoPath);
            if (!validation.IsValid)
            {
                return new { error = validation.Error };
            }

            count = Math.Clamp(count, 1, 100);
            var resolvedPath = validation.ResolvedPath!;
            var format = "--format=%H|%h|%an|%ae|%ai|%s";
            var result = await RunGitCommandAsync(["log", $"-{count}", format], resolvedPath);

            if (result.exitCode != 0)
            {
                return new { error = $"Git log failed: {result.stderr}" };
            }

            var commits = result.stdout
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line =>
                {
                    var parts = line.Split('|', 6);
                    if (parts.Length < 6) return null;
                    return (object)new
                    {
                        hash = parts[0],
                        short_hash = parts[1],
                        author = parts[2],
                        email = parts[3],
                        date = parts[4],
                        message = parts[5]
                    };
                })
                .Where(c => c != null)
                .ToList();

            return new
            {
                commits,
                count = commits.Count
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to get git log");
            return new { error = $"Failed to get git log: {ex.Message}" };
        }
    }

    /// <summary>
    /// 执行 git 命令 — 委托给 GitProcessHelper（统一超时+截断行为）
    /// </summary>
    private static Task<(int exitCode, string stdout, string stderr)> RunGitCommandAsync(string[] args, string workingDirectory)
        => GitProcessHelper.RunGitCommandAsync(args, workingDirectory);
}
