namespace Tnzi.AI.Cli.Hosting;

/// <summary>
/// 查询一个 CLI 在给定环境下是否已登录。
/// </summary>
public interface ICliAuthStatusProbe
{
    /// <summary>
    /// 在 <paramref name="host"/> 上以 <paramref name="spec"/> 描述的可执行文件、参数、工作目录与环境运行一次登录状态查询。
    /// </summary>
    /// <returns>true = 已登录；false = 明确未登录；null = 查不出来（启动失败、超时、输出不可解析）。</returns>
    Task<bool?> IsSignedInAsync(ICliProcessHost host, CliProcessSpec spec, CancellationToken cancellationToken);
}

/// <inheritdoc cref="ICliAuthStatusProbe" />
/// <remarks>
/// <para>
/// 环境与真正的运行<b>同源</b>（同一个执行目标、同一份白名单与显式变量）：在别处查，看到的是别的账号的登录态，
/// 而运行看到的可能是另一个配置目录、或少了一个没进白名单的 API key，那样答出来的「已登录」对运行毫无意义。
/// </para>
/// <para>查询经 <see cref="CliHostCommand"/> 走执行目标的 <see cref="ICliProcessHost.StartAsync"/>，远程目标不需要另实现一套。</para>
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
    public async Task<bool?> IsSignedInAsync(ICliProcessHost host, CliProcessSpec spec, CancellationToken cancellationToken)
    {
        Check.NotNull(host);
        Check.NotNull(spec);

        var result = await CliHostCommand.RunAsync(host, spec, ProbeTimeout, _logger, cancellationToken);
        if (result is null)
        {
            return null;
        }

        var signedIn = ParseSignedIn(result.Output);
        if (signedIn is null)
        {
            _logger.LogDebug(
                "Auth status probe for {Executable} exited {ExitCode} without a readable 'loggedIn' field",
                spec.ExecutablePath, result.ExitCode);
        }

        return signedIn;
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
}
