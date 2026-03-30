namespace Tnzi.AI.Coder.Repl;

/// <summary>
/// 代码执行工具组 — 在沙箱中运行代码片段
/// </summary>
[AIToolGroup("repl", "Code Execution", "Execute code snippets in various languages")]
public class ReplTools : IAIToolProvider
{
    private readonly ICommandSanitizer _commandSanitizer;
    private readonly IToolApprovalHandler? _approvalHandler;
    private readonly CoderOptions _options;
    private readonly ILogger<ReplTools> _logger;

    /// <summary>
    /// 支持的语言及对应的文件扩展名和执行命令
    /// </summary>
    private static readonly Dictionary<string, (string Extension, string[] Commands)> SupportedLanguages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["csharp"] = (".csx", ["dotnet-script", "dotnet script"]),
        ["python"] = (".py", OperatingSystem.IsWindows() ? ["python", "python3"] : ["python3", "python"]),
        ["javascript"] = (".js", ["node"]),
        ["typescript"] = (".ts", ["npx tsx"])
    };

    public ReplTools(
        ICommandSanitizer commandSanitizer,
        IOptions<CoderOptions> options,
        ILogger<ReplTools> logger,
        IToolApprovalHandler? approvalHandler = null)
    {
        _commandSanitizer = Check.NotNull(commandSanitizer);
        _options = Check.NotNull(options).Value;
        _logger = Check.NotNull(logger);
        _approvalHandler = approvalHandler;
    }

    /// <summary>
    /// 执行代码片段
    /// </summary>
    [AIFunction("execute_code", "Execute a code snippet in a given language")]
    public async Task<object> ExecuteCodeAsync(
        [AIParameter("code", "The code to execute")] string code,
        [AIParameter("language", "Programming language: csharp, python, javascript, typescript")] string language,
        [AIParameter("timeout_ms", "Timeout in milliseconds (default: 30000)", false)] int? timeoutMs = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return new { error = "Code cannot be empty" };
            }

            if (!SupportedLanguages.TryGetValue(language, out var langConfig))
            {
                return new { error = $"Unsupported language: {language}. Supported: {string.Join(", ", SupportedLanguages.Keys)}" };
            }

            // 通过 ICommandSanitizer 检查安全性
            var shellCommand = $"[execute_code:{language}]";
            var sanitizeResult = _commandSanitizer.Sanitize(shellCommand);
            if (!sanitizeResult.IsAllowed)
            {
                return new { error = $"Code execution denied: {sanitizeResult.Reason}" };
            }

            // 审批流
            if (sanitizeResult.RequiresApproval && _approvalHandler != null)
            {
                var codePreview = code.Length > 500 ? code[..500] + "..." : code;
                var approvalRequest = new ToolApprovalRequest
                {
                    ToolName = "execute_code",
                    ToolGroup = "repl",
                    ToolDescription = $"Execute {language} code snippet",
                    Arguments = new Dictionary<string, object?>
                    {
                        ["language"] = language,
                        ["code"] = codePreview
                    },
                    Reason = $"Execute {language} code ({code.Length} chars)"
                };

                var approvalResult = await _approvalHandler.RequestApprovalAsync(approvalRequest);
                if (!approvalResult.Approved)
                {
                    return new
                    {
                        error = $"Code execution rejected: {approvalResult.RejectionReason ?? "Not approved"}",
                        status = approvalResult.Status.ToString()
                    };
                }
            }

            // 创建临时文件
            var tempFile = Path.Combine(Path.GetTempPath(), $"tnzi_repl_{Guid.NewGuid():N}{langConfig.Extension}");
            try
            {
                await File.WriteAllTextAsync(tempFile, code);

                // 查找可用的执行器
                var (executableCommand, found) = await FindExecutableAsync(langConfig.Commands);
                if (!found)
                {
                    return new { error = $"No {language} runtime found. Tried: {string.Join(", ", langConfig.Commands)}" };
                }

                var timeout = Math.Min(timeoutMs ?? 30_000, 600_000); // 最大 10 分钟

                _logger.LogDebug("Executing {Language} code ({Length} chars) with {Command}, timeout: {Timeout}ms",
                    language, code.Length, executableCommand, timeout);

                var sw = Stopwatch.StartNew();
                var (exitCode, stdout, stderr) = await RunProcessAsync(executableCommand, tempFile, timeout);
                sw.Stop();

                stdout = TruncateOutput(stdout);
                stderr = TruncateOutput(stderr);

                _logger.LogDebug("Code execution completed: exit_code={ExitCode}, duration={Duration}ms",
                    exitCode, sw.ElapsedMilliseconds);

                return new
                {
                    stdout,
                    stderr,
                    exit_code = exitCode,
                    language,
                    duration_ms = sw.ElapsedMilliseconds
                };
            }
            finally
            {
                // 清理临时文件
                try { File.Delete(tempFile); }
                catch { /* 尽力清理 */ }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to execute {Language} code", language);
            return new { error = $"Failed to execute code: {ex.Message}" };
        }
    }

    /// <summary>
    /// 执行 SQL 查询（存根 — 需要数据库配置）
    /// </summary>
    [AIFunction("execute_sql", "Execute a SQL query against a configured database", Enabled = false)]
    public Task<object> ExecuteSqlAsync(
        [AIParameter("query", "SQL query to execute")] string query,
        [AIParameter("connection_name", "Connection name from configuration", false)] string? connectionName = null)
    {
        return Task.FromResult<object>(new
        {
            error = "SQL execution requires database configuration. Use bash tool with your database CLI instead."
        });
    }

    /// <summary>
    /// 查找可用的执行器命令
    /// </summary>
    private static async Task<(string Command, bool Found)> FindExecutableAsync(string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            // 分离命令和参数（如 "npx tsx"）
            var parts = candidate.Split(' ', 2);
            var executable = parts[0];

            var whichCommand = OperatingSystem.IsWindows() ? "where" : "which";
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = whichCommand,
                    Arguments = executable,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = psi };
                process.Start();

                using var cts = new CancellationTokenSource(5000);
                await process.WaitForExitAsync(cts.Token);

                if (process.ExitCode == 0)
                {
                    return (candidate, true);
                }
            }
            catch
            {
                // 该候选不可用，尝试下一个
            }
        }

        return (string.Empty, false);
    }

    /// <summary>
    /// 运行外部进程执行代码文件
    /// </summary>
    private async Task<(int ExitCode, string Stdout, string Stderr)> RunProcessAsync(string command, string filePath, int timeoutMs)
    {
        // 分离命令和额外参数（如 "npx tsx" → FileName="npx", args="tsx {file}"）
        var parts = command.Split(' ', 2);
        var fileName = parts[0];
        var extraArgs = parts.Length > 1 ? parts[1] + " " : "";

        var shellPath = OperatingSystem.IsWindows() ? "bash" : "/bin/bash";
        var fullCommand = $"{fileName} {extraArgs}\"{filePath}\"";

        var psi = new ProcessStartInfo
        {
            FileName = shellPath,
            WorkingDirectory = Path.GetTempPath(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(fullCommand);

        EnvironmentFilter.ApplyEnvironmentFilter(psi, _options.Sandbox);

        var maxBufferSize = _options.Sandbox.MaxOutputSize;

        using var process = new Process { StartInfo = psi };
        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null && stdoutBuilder.Length < maxBufferSize)
            {
                stdoutBuilder.AppendLine(e.Data);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null && stderrBuilder.Length < maxBufferSize)
            {
                stderrBuilder.AppendLine(e.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); }
            catch { /* 尽力终止 */ }
            return (-1, stdoutBuilder.ToString().TrimEnd(), $"Code execution timed out after {timeoutMs}ms");
        }

        return (process.ExitCode, stdoutBuilder.ToString().TrimEnd(), stderrBuilder.ToString().TrimEnd());
    }

    private string TruncateOutput(string output) => OutputHelper.Truncate(output, _options.Sandbox.MaxOutputSize);
}
