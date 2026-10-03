namespace Tnzi.AI.Cli.Hosting;

/// <summary>
/// 查询一个 CLI 在给定环境下是否已登录。
/// </summary>
public interface ICliAuthStatusProbe
{
    /// <summary>
    /// 以 <paramref name="spec"/> 描述的可执行文件、参数、工作目录与环境运行一次登录状态查询。
    /// </summary>
    /// <returns>true = 已登录；false = 明确未登录；null = 查不出来（启动失败、超时、输出不可解析）。</returns>
    Task<bool?> IsSignedInAsync(CliProcessSpec spec, CancellationToken cancellationToken);
}

/// <inheritdoc cref="ICliAuthStatusProbe" />
/// <remarks>
/// 环境与真正的运行<b>同源</b>（同一个 <see cref="CliEnvironmentBuilder"/>，同一份白名单与显式变量）：
/// 在宿主进程自己的环境里查，看到的是宿主账号的登录态，而运行看到的可能是另一个配置目录、
/// 或少了一个没进白名单的 API key —— 那样答出来的「已登录」对运行毫无意义。
/// </remarks>
public class CliAuthStatusProbe : ICliAuthStatusProbe
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(15);

    private readonly ILogger<CliAuthStatusProbe> _logger;

    /// <summary>初始化登录状态探针。</summary>
    public CliAuthStatusProbe(ILogger<CliAuthStatusProbe> logger)
    {
        _logger = Check.NotNull(logger);
    }

    /// <inheritdoc />
    public async Task<bool?> IsSignedInAsync(CliProcessSpec spec, CancellationToken cancellationToken)
    {
        Check.NotNull(spec);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = spec.ExecutablePath,
                WorkingDirectory = spec.WorkingDirectory,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            }
        };

        foreach (var argument in spec.Arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.StartInfo.Environment.Clear();
        foreach (var (key, value) in CliEnvironmentBuilder.Build(spec))
        {
            process.StartInfo.Environment[key] = value;
        }

        try
        {
            if (!process.Start())
            {
                return null;
            }

            process.StandardInput.Close();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(ProbeTimeout);

            var errorTask = process.StandardError.ReadToEndAsync(cts.Token);
            var output = await process.StandardOutput.ReadToEndAsync(cts.Token);
            await process.WaitForExitAsync(cts.Token);
            await errorTask;

            var signedIn = ParseSignedIn(output);
            if (signedIn is null)
            {
                _logger.LogDebug(
                    "Auth status probe for {Executable} exited {ExitCode} without a readable 'loggedIn' field",
                    spec.ExecutablePath, process.ExitCode);
            }

            return signedIn;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug("Auth status probe for {Executable} timed out; killing it", spec.ExecutablePath);
            TryKill(process);
            return null;
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or SystemException)
        {
            _logger.LogDebug(ex, "Auth status probe for {Executable} could not run", spec.ExecutablePath);
            return null;
        }
    }

    /// <summary>
    /// 从查询输出里读 <c>loggedIn</c>。输出不是 JSON、不含该字段或它不是布尔值时返回 null。
    /// </summary>
    internal static bool? ParseSignedIn(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(output);
            return document.RootElement.ValueKind == JsonValueKind.Object
                   && document.RootElement.TryGetProperty("loggedIn", out var value)
                   && value.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? value.GetBoolean()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or SystemException)
        {
            _logger.LogDebug(ex, "Could not kill the timed-out auth status probe");
        }
    }
}
