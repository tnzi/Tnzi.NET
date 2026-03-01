namespace Tnzi.AI.Coder.Git;

/// <summary>
/// Git 操作工具组 — 版本控制写操作（diff、stage、commit、branch 等）
/// </summary>
[AIToolGroup("git", "Git Operations", "Git version control operations")]
public class GitTools : IAIToolProvider
{
    private readonly IPathValidator _pathValidator;
    private readonly ICommandSanitizer _commandSanitizer;
    private readonly IToolApprovalHandler? _approvalHandler;
    private readonly CoderOptions _options;
    private readonly ILogger<GitTools> _logger;

    public GitTools(
        IPathValidator pathValidator,
        ICommandSanitizer commandSanitizer,
        IOptions<CoderOptions> options,
        ILogger<GitTools> logger,
        IToolApprovalHandler? approvalHandler = null)
    {
        _pathValidator = Check.NotNull(pathValidator);
        _commandSanitizer = Check.NotNull(commandSanitizer);
        _approvalHandler = approvalHandler;
        _options = Check.NotNull(options).Value;
        _logger = Check.NotNull(logger);
    }

    /// <summary>
    /// 查看工作树变更（支持 staged、指定文件、分支比较）
    /// </summary>
    [AIFunction("git_diff", "Show working tree changes")]
    public async Task<object> GitDiffAsync(
        [AIParameter("path", "Repository path", false)] string? path = null,
        [AIParameter("staged", "Show staged changes only", false)] bool staged = false,
        [AIParameter("file", "Specific file to diff", false)] string? file = null,
        [AIParameter("base_branch", "Compare with a branch (e.g. main...HEAD)", false)] string? baseBranch = null)
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

            // 构建 diff 命令参数
            var args = new List<string> { "diff" };

            if (staged)
            {
                args.Add("--cached");
            }

            if (!string.IsNullOrEmpty(baseBranch))
            {
                // 验证分支名，防止命令注入
                if (!IsValidRefName(baseBranch))
                {
                    return new { error = "Invalid branch name" };
                }
                args.Add(baseBranch);
            }

            if (!string.IsNullOrEmpty(file))
            {
                args.Add("--");
                args.Add(file);
            }

            var result = await RunGitCommandAsync(args.ToArray(), resolvedPath);

            if (result.exitCode != 0)
            {
                return new { error = $"Git diff failed: {result.stderr}" };
            }

            _logger.LogDebug("Git diff in '{Path}' (staged={Staged}, file={File})", repoPath, staged, file);

            return new
            {
                diff = result.stdout,
                has_changes = !string.IsNullOrEmpty(result.stdout)
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to run git diff");
            return new { error = $"Failed to run git diff: {ex.Message}" };
        }
    }

    /// <summary>
    /// 将文件暂存到索引区
    /// </summary>
    [AIFunction("git_add", "Stage files for commit")]
    public async Task<object> GitAddAsync(
        [AIParameter("files", "Files to stage (comma-separated), or '.' for all")] string files,
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

            // 构建 add 命令
            List<string> args;
            if (files.Trim() == ".")
            {
                args = ["add", "."];
            }
            else
            {
                var fileList = files.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (fileList.Length == 0)
                {
                    return new { error = "No files specified" };
                }
                args = ["add", "--"];
                args.AddRange(fileList);
            }

            var result = await RunGitCommandAsync(args.ToArray(), resolvedPath);

            if (result.exitCode != 0)
            {
                return new { error = $"Git add failed: {result.stderr}" };
            }

            _logger.LogDebug("Git add '{Files}' in '{Path}'", files, repoPath);

            return new
            {
                success = true,
                files_staged = files
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to run git add");
            return new { error = $"Failed to run git add: {ex.Message}" };
        }
    }

    /// <summary>
    /// 创建提交
    /// </summary>
    [AIFunction("git_commit", "Create a commit with message")]
    public async Task<object> GitCommitAsync(
        [AIParameter("message", "Commit message")] string message,
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

            if (string.IsNullOrWhiteSpace(message))
            {
                return new { error = "Commit message cannot be empty" };
            }

            // 审批流：写操作需要审批
            var approvalError = await RequestWriteApprovalAsync("git_commit", "commit", new Dictionary<string, object?> { ["message"] = message, ["path"] = repoPath });
            if (approvalError != null) return approvalError;

            var result = await RunGitCommandAsync(["commit", "-m", message], resolvedPath);

            if (result.exitCode != 0)
            {
                return new { error = $"Git commit failed: {result.stderr}" };
            }

            _logger.LogDebug("Git commit in '{Path}': {Message}", repoPath, message);

            return new
            {
                success = true,
                output = result.stdout
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to run git commit");
            return new { error = $"Failed to run git commit: {ex.Message}" };
        }
    }

    /// <summary>
    /// 切换分支或恢复文件
    /// </summary>
    [AIFunction("git_checkout", "Switch branches or restore files")]
    public async Task<object> GitCheckoutAsync(
        [AIParameter("target", "Branch name or file path to checkout")] string target,
        [AIParameter("create", "Create a new branch (-b)", false)] bool create = false,
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

            if (!IsValidRefName(target))
            {
                return new { error = "Invalid branch or file name" };
            }

            // 审批流：写操作需要审批
            var approvalError = await RequestWriteApprovalAsync("git_checkout", "checkout", new Dictionary<string, object?> { ["target"] = target, ["create"] = create, ["path"] = repoPath });
            if (approvalError != null) return approvalError;

            var args = create
                ? new[] { "checkout", "-b", target }
                : new[] { "checkout", target };

            var result = await RunGitCommandAsync(args, resolvedPath);

            if (result.exitCode != 0)
            {
                return new { error = $"Git checkout failed: {result.stderr}" };
            }

            _logger.LogDebug("Git checkout '{Target}' (create={Create}) in '{Path}'", target, create, repoPath);

            return new
            {
                success = true,
                output = !string.IsNullOrEmpty(result.stdout) ? result.stdout : result.stderr
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to run git checkout");
            return new { error = $"Failed to run git checkout: {ex.Message}" };
        }
    }

    /// <summary>
    /// 分支操作（列出、创建、删除）
    /// </summary>
    [AIFunction("git_branch", "List, create, or delete branches")]
    public async Task<object> GitBranchAsync(
        [AIParameter("name", "Branch name (for create/delete)", false)] string? name = null,
        [AIParameter("delete", "Delete the branch", false)] bool delete = false,
        [AIParameter("all", "List all branches including remotes", false)] bool all = false,
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

            string[] args;
            if (!string.IsNullOrEmpty(name))
            {
                if (!IsValidRefName(name))
                {
                    return new { error = "Invalid branch name" };
                }

                // 删除分支是写操作，需要审批
                if (delete)
                {
                    var approvalError = await RequestWriteApprovalAsync("git_branch", "branch -d", new Dictionary<string, object?> { ["name"] = name, ["path"] = repoPath });
                    if (approvalError != null) return approvalError;
                }

                args = delete
                    ? ["branch", "-d", name]
                    : ["branch", name];
            }
            else
            {
                args = all ? ["branch", "-a"] : ["branch"];
            }

            var result = await RunGitCommandAsync(args, resolvedPath);

            if (result.exitCode != 0)
            {
                return new { error = $"Git branch failed: {result.stderr}" };
            }

            // 列出模式：解析分支列表
            if (string.IsNullOrEmpty(name))
            {
                var branches = result.stdout
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Select(b =>
                    {
                        var isCurrent = b.StartsWith('*');
                        var branchName = b.TrimStart('*', ' ');
                        return new { name = branchName, current = isCurrent };
                    })
                    .ToList();

                _logger.LogDebug("Git branch list in '{Path}': {Count} branches", repoPath, branches.Count);

                return new
                {
                    branches,
                    count = branches.Count
                };
            }

            _logger.LogDebug("Git branch '{Name}' (delete={Delete}) in '{Path}'", name, delete, repoPath);

            return new
            {
                success = true,
                output = result.stdout
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to run git branch");
            return new { error = $"Failed to run git branch: {ex.Message}" };
        }
    }

    /// <summary>
    /// 暂存/恢复/查看变更
    /// </summary>
    [AIFunction("git_stash", "Stash, pop, or list changes")]
    public async Task<object> GitStashAsync(
        [AIParameter("action", "Action: push, pop, list, drop", false)] string action = "push",
        [AIParameter("message", "Stash message (for push)", false)] string? message = null,
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

            var args = action.ToLowerInvariant() switch
            {
                "push" => !string.IsNullOrEmpty(message)
                    ? new[] { "stash", "push", "-m", message }
                    : new[] { "stash", "push" },
                "pop" => new[] { "stash", "pop" },
                "list" => new[] { "stash", "list" },
                "drop" => new[] { "stash", "drop" },
                _ => null as string[]
            };

            if (args == null)
            {
                return new { error = $"Unknown stash action: {action}. Valid actions: push, pop, list, drop" };
            }

            var result = await RunGitCommandAsync(args, resolvedPath);

            if (result.exitCode != 0)
            {
                return new { error = $"Git stash {action} failed: {result.stderr}" };
            }

            _logger.LogDebug("Git stash {Action} in '{Path}'", action, repoPath);

            // list 模式返回解析后的列表
            if (action.Equals("list", StringComparison.OrdinalIgnoreCase))
            {
                var entries = result.stdout
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .ToList();

                return new
                {
                    entries,
                    count = entries.Count
                };
            }

            return new
            {
                success = true,
                output = result.stdout
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to run git stash");
            return new { error = $"Failed to run git stash: {ex.Message}" };
        }
    }

    /// <summary>
    /// 取消暂存文件或重置到指定提交
    /// </summary>
    [AIFunction("git_reset", "Unstage files or reset to a commit")]
    public async Task<object> GitResetAsync(
        [AIParameter("files", "Files to unstage (comma-separated)", false)] string? files = null,
        [AIParameter("commit", "Commit to reset to (soft reset)", false)] string? commit = null,
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

            // 审批流：写操作需要审批
            var approvalError = await RequestWriteApprovalAsync("git_reset", "reset", new Dictionary<string, object?> { ["files"] = files, ["commit"] = commit, ["path"] = repoPath });
            if (approvalError != null) return approvalError;

            string[] args;
            if (!string.IsNullOrEmpty(files))
            {
                // 取消暂存指定文件
                var fileList = files.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (fileList.Length == 0)
                {
                    return new { error = "No files specified" };
                }
                var argsList = new List<string> { "reset", "HEAD", "--" };
                argsList.AddRange(fileList);
                args = argsList.ToArray();
            }
            else if (!string.IsNullOrEmpty(commit))
            {
                // 重置到指定提交（soft reset，保留工作区和暂存区修改）
                if (!IsValidRefName(commit))
                {
                    return new { error = "Invalid commit reference" };
                }
                args = ["reset", "--soft", commit];
            }
            else
            {
                // 默认：取消所有暂存
                args = ["reset", "HEAD"];
            }

            var result = await RunGitCommandAsync(args, resolvedPath);

            if (result.exitCode != 0)
            {
                return new { error = $"Git reset failed: {result.stderr}" };
            }

            _logger.LogDebug("Git reset in '{Path}' (files={Files}, commit={Commit})", repoPath, files, commit);

            return new
            {
                success = true,
                output = result.stdout
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to run git reset");
            return new { error = $"Failed to run git reset: {ex.Message}" };
        }
    }

    /// <summary>
    /// 请求写操作审批（硬拒绝 + 审批流）
    /// </summary>
    private async Task<object?> RequestWriteApprovalAsync(string toolName, string operationName, Dictionary<string, object?> arguments)
    {
        var sanitizeResult = _commandSanitizer.Sanitize($"git {operationName}");
        if (!sanitizeResult.IsAllowed)
        {
            return new { error = $"Command denied: {sanitizeResult.Reason}" };
        }

        if (sanitizeResult.RequiresApproval && _approvalHandler != null)
        {
            var approval = await _approvalHandler.RequestApprovalAsync(new ToolApprovalRequest
            {
                ToolName = toolName,
                ToolGroup = "git",
                ToolDescription = $"Git {operationName} operation",
                Arguments = arguments,
                Reason = sanitizeResult.Reason
            });

            if (!approval.Approved)
            {
                return new { error = $"Command rejected: {approval.RejectionReason ?? "Not approved"}" };
            }
        }

        return null;
    }

    /// <summary>
    /// 验证 Git 引用名是否合法（防止命令注入）
    /// </summary>
    private static bool IsValidRefName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        if (name.StartsWith('-')) return false;
        return Regex.IsMatch(name, @"^[a-zA-Z0-9/_.\-]+$");
    }

    /// <summary>
    /// 执行 git 命令 — 委托给 GitProcessHelper
    /// </summary>
    private static Task<(int exitCode, string stdout, string stderr)> RunGitCommandAsync(string[] args, string workingDirectory)
        => GitProcessHelper.RunGitCommandAsync(args, workingDirectory);
}
