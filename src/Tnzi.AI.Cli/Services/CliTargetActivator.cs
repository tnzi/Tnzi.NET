namespace Tnzi.AI.Cli.Services;

/// <summary>
/// 运行排在一个不在线的远程执行目标上时（例如用户的工作区容器停着），由使用方把它起来。
/// </summary>
/// <remarks>
/// <para>
/// 框架发现某个远程运行时上有运行在等、而没有任何服务端副本服务它（<see cref="ICliHost.GetCoverage"/>）时调用；同一个目标在
/// <c>AI:Cli:Activation:RetryInterval</c> 内只问一次（多个副本之间按运行时记录去重）。目标起来之后它的代理连回服务端、
/// 宿主把它列进覆盖范围，那个副本就认领这条运行。从第一次唤起起过了 <c>AI:Cli:Activation:Timeout</c> 还没上线，
/// 排在它上面的运行以 <see cref="CliRunFailureReason.TargetUnavailable"/> 结束。
/// </para>
/// <para>
/// 发出启动请求就返回，不要等目标上线：这里在队列循环里调用。重复调用必须无害（目标已经在起或已经起来）。
/// 在消费方模块里注册为 Scoped；没有注册时框架不唤起，只等目标自己上线。抛出的异常记 Warning，下一个间隔再试。
/// </para>
/// </remarks>
[ExperimentalApi(Reason = "Per-workspace runtimes are new")]
public interface ICliTargetActivator
{
    /// <summary>请执行目标上线。</summary>
    Task<CliActivationResult> ActivateAsync(CliActivationRequest request, CancellationToken cancellationToken);
}

/// <summary>要唤起的执行目标，以及在等它的那条运行（排在最前面的一条）的身份。</summary>
public sealed record CliActivationRequest
{
    /// <summary>执行目标标识（远程运行时的 <c>HostId</c>）。</summary>
    public required string TargetId { get; init; }

    /// <summary>运行时记录。</summary>
    public required Guid RuntimeId { get; init; }

    /// <summary>provider 键。</summary>
    public required string ProviderKey { get; init; }

    /// <summary>在等的运行。</summary>
    public Guid? RunId { get; init; }

    /// <summary>那条运行的租户。</summary>
    public Guid? TenantId { get; init; }

    /// <summary>那条运行的派出者。</summary>
    public Guid? UserId { get; init; }

    /// <summary>那条运行的线程。</summary>
    public Guid? ThreadId { get; init; }
}

/// <summary>唤起的回答。</summary>
public sealed record CliActivationResult
{
    /// <summary>已请求启动（或目标已经在起）：框架等它上线。</summary>
    public static CliActivationResult Started { get; } = new();

    /// <summary>不会起它（例如这个用户没有工作区、账户被停用）：在它上面排着的运行当场以 <c>TargetUnavailable</c> 结束。</summary>
    /// <param name="reason">给使用者看的英文原因。</param>
    public static CliActivationResult Refused(string reason) => new() { RefusalReason = Check.NotNullOrWhiteSpace(reason) };

    /// <summary>拒绝的原因；null = 已请求启动。</summary>
    public string? RefusalReason { get; private init; }
}
