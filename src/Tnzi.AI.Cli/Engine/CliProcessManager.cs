namespace Tnzi.AI.Cli.Engine;

/// <summary>
/// CLI 子进程生命周期管理器
/// </summary>
public class CliProcessManager
{
    private readonly ILogger<CliProcessManager> _logger;

    public CliProcessManager(ILogger<CliProcessManager> logger)
    {
        _logger = Check.NotNull(logger);
    }

    /// <summary>
    /// 启动 CLI 子进程并流式返回事件
    /// </summary>
    public async IAsyncEnumerable<CliAgentEvent> ExecuteAsync(
        ICliAgentProvider provider,
        ProcessStartInfo psi,
        string prompt,
        int timeoutSeconds,
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        var linkedToken = linkedCts.Token;

        _logger.LogInformation("Starting CLI process: {FileName} in {WorkingDirectory}",
            psi.FileName, psi.WorkingDirectory);

        using var process = new Process { StartInfo = psi };

        // 启动进程 — yield 不能在 catch 中，所以捕获错误后在外层 yield
        CliAgentEvent? startError = null;
        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start CLI process: {FileName}", psi.FileName);
            startError = new CliAgentEvent
            {
                EventType = CliEventType.Error,
                Content = $"Failed to start CLI process '{psi.FileName}': {ex.Message}",
                IsError = true
            };
        }

        if (startError != null)
        {
            yield return startError;
            yield break;
        }

        // 通过 stdin 传入 prompt
        CliAgentEvent? stdinError = null;
        try
        {
            await process.StandardInput.WriteAsync(prompt.AsMemory(), linkedToken);
            process.StandardInput.Close();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write prompt to stdin");
            KillProcess(process);
            stdinError = new CliAgentEvent
            {
                EventType = CliEventType.Error,
                Content = $"Failed to write prompt to stdin: {ex.Message}",
                IsError = true
            };
        }

        if (stdinError != null)
        {
            yield return stdinError;
            yield break;
        }

        // 并发读取 stderr
        var stderrBuilder = new StringBuilder();
        var stderrTask = ReadStderrAsync(process.StandardError, stderrBuilder, linkedToken);

        // 流式解析 stdout 事件
        await foreach (var evt in provider.ParseOutputAsync(process.StandardOutput, linkedToken))
        {
            yield return evt;
        }

        try { await stderrTask; }
        catch (OperationCanceledException) { /* 正常取消 */ }

        if (!process.HasExited) KillProcess(process);
        try { await process.WaitForExitAsync(CancellationToken.None); }
        catch { /* 忽略等待异常 */ }

        var exitCode = process.HasExited ? process.ExitCode : -1;
        _logger.LogInformation("CLI process exited with code {ExitCode}", exitCode);

        if (exitCode != 0 && stderrBuilder.Length > 0)
        {
            yield return new CliAgentEvent
            {
                EventType = CliEventType.Error,
                Content = stderrBuilder.ToString(),
                IsError = true
            };
        }
    }

    private static async Task ReadStderrAsync(StreamReader stderr, StringBuilder builder, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            string? line;
            try { line = await stderr.ReadLineAsync(ct); }
            catch (OperationCanceledException) { break; }
            if (line is null) break;
            if (!string.IsNullOrWhiteSpace(line)) builder.AppendLine(line);
        }
    }

    private void KillProcess(Process process)
    {
        try { process.Kill(entireProcessTree: true); }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to kill CLI process tree"); }
    }
}
