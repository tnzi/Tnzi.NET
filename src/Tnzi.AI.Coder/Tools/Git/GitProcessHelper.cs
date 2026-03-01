namespace Tnzi.AI.Coder.Git;

/// <summary>
/// Git 进程执行帮助器 — GitTools 和 ProjectTools 共享的 git 命令执行逻辑
/// </summary>
internal static class GitProcessHelper
{
    private const int MaxOutputSize = 100_000;

    /// <summary>
    /// 执行 git 命令（30 秒超时，输出截断至 100KB）
    /// </summary>
    public static async Task<(int exitCode, string stdout, string stderr)> RunGitCommandAsync(string[] args, string workingDirectory)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var process = new Process { StartInfo = psi };
        process.Start();

        // 并行读取 stdout 和 stderr，避免死锁
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cts.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(cts.Token);

        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); }
            catch { /* best effort */ }
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        // 截断过大输出
        if (stdout.Length > MaxOutputSize)
            stdout = stdout[..MaxOutputSize] + "\n... (truncated)";
        if (stderr.Length > MaxOutputSize)
            stderr = stderr[..MaxOutputSize] + "\n... (truncated)";

        if (cts.IsCancellationRequested && !process.HasExited)
        {
            return (-1, stdout.Trim(), "Git command timed out after 30 seconds");
        }

        return (process.ExitCode, stdout.Trim(), stderr.Trim());
    }
}
