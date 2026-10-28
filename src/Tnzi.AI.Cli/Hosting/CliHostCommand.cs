namespace Tnzi.AI.Cli.Hosting;

/// <summary>
/// 在任一执行目标上跑一条读完输出就结束的短命令（版本号、登录态）。
/// </summary>
/// <remarks>
/// 只用 <see cref="ICliProcessHost.StartAsync"/>：远程宿主只需要会起进程，探测就和会话走同一条路，
/// 整树终止、环境白名单、批处理参数检查也一并生效。stdin 起来就关，stdout 读到 EOF，超时由释放进程时的整树终止收尾：
/// 卡住的探测进程不能留在那里，每个探测周期泄漏一个，几天后就是一片僵尸。
/// </remarks>
public static class CliHostCommand
{
    /// <summary>
    /// 运行 <paramref name="spec"/> 并收集 stdout。
    /// </summary>
    /// <returns>退出码与 stdout；起不来、超时时为 null。调用方的取消照常抛出。</returns>
    public static async Task<CliHostCommandResult?> RunAsync(
        ICliProcessHost host, CliProcessSpec spec, TimeSpan timeout, ILogger logger, CancellationToken cancellationToken)
    {
        Check.NotNull(host);
        Check.NotNull(spec);
        Check.NotNull(logger);

        ICliProcess process;
        try
        {
            process = await host.StartAsync(spec with { Purpose = CliProcessPurpose.Probe }, cancellationToken);
        }
        catch (Exception ex) when (ex is TnziException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            logger.LogDebug(ex, "Probe command {Executable} could not be started on {Host}", spec.ExecutablePath, host.Name);
            return null;
        }

        await using (process)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);

            try
            {
                await process.Transport.CloseInputAsync();

                var output = new StringBuilder();
                await foreach (var line in process.Transport.ReadLinesAsync(cts.Token))
                {
                    output.Append(line).Append('\n');
                }

                var exitCode = await process.WaitForExitAsync(cts.Token);
                return new CliHostCommandResult(exitCode, output.ToString());
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogDebug("Probe command {Executable} timed out on {Host}; terminating it", spec.ExecutablePath, host.Name);
                return null;
            }
        }
    }

    /// <summary>
    /// 探测 CLI 版本（<c>--version</c> 的第一行）。<b>仅供观测</b>，绝不用于选择行为分支。
    /// </summary>
    public static async Task<string?> DetectVersionAsync(
        ICliProcessHost host, string executablePath, ILogger logger, CancellationToken cancellationToken)
    {
        Check.NotNullOrWhiteSpace(executablePath);

        // 透传执行位置的全部环境：npm 装的 CLI 是 node 脚本，少了 PATH 之外的变量也可能起不来，而 --version 不碰任何机密
        var result = await RunAsync(host, new CliProcessSpec
        {
            ExecutablePath = executablePath,
            Arguments = ["--version"],
            InheritAllHostEnvironment = true
        }, VersionProbeTimeout, logger, cancellationToken);

        var version = result?.Output.Trim();
        return string.IsNullOrEmpty(version) ? null : version.Split('\n')[0].Trim();
    }

    private static readonly TimeSpan VersionProbeTimeout = TimeSpan.FromSeconds(10);
}

/// <summary>
/// 一条短命令的结果。
/// </summary>
/// <param name="ExitCode">退出码。</param>
/// <param name="Output">stdout（逐行以 <c>\n</c> 连接）。</param>
public sealed record CliHostCommandResult(int ExitCode, string Output);
