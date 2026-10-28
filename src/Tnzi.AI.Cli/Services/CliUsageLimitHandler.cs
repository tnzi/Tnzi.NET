namespace Tnzi.AI.Cli.Services;

/// <summary>
/// 一次执行停在 CLI 背后账号的用量上限上（<see cref="CliRunFailureReason.UsageLimit"/>）时，由消费方决定换个登录再接着跑。
/// </summary>
/// <remarks>
/// <para>
/// 框架自己不知道还有别的账号：登录态来自部署配置或消费方的 <see cref="ICliLaunchEnvironmentComposer"/>。
/// 消费方在这里把「这个账号到了上限」记下来（通常连同重置时间），让下一次启动换用另一份凭据，再答 <c>true</c>；
/// 执行器随即在同一条运行里接着跑一次（运行保持 Running，见 docs/modules/ai-cli.md §5.1.4）。
/// </para>
/// <para>
/// 每条运行最多因此重试一次：换上的凭据再到上限，这一轮就以 UsageLimit 结束。没有注册实现时行为不变。
/// 实现以作用域服务解析，在执行器自己的作用域里调用；抛出的异常记日志后按「不重试」处理。
/// </para>
/// </remarks>
public interface ICliUsageLimitHandler
{
    /// <summary>
    /// 账号到了上限。返回 <c>true</c> 表示下一次启动会以另一份登录运行，可以接着跑。
    /// </summary>
    Task<bool> OnUsageLimitAsync(CliUsageLimitContext context, CancellationToken cancellationToken);
}

/// <summary>停在用量上限上的那一次执行。</summary>
public sealed record CliUsageLimitContext
{
    /// <summary>运行 id。</summary>
    public required Guid RunId { get; init; }

    /// <summary>运行所属的 Agent。</summary>
    public required Guid AgentId { get; init; }

    /// <summary>运行所在的线程；不属于线程的运行为 null。</summary>
    public Guid? ThreadId { get; init; }

    /// <summary>派出运行的用户；后台派发为 null。</summary>
    public Guid? CreatorId { get; init; }

    /// <summary>provider 键（如 <c>claude</c>）。</summary>
    public required string ProviderKey { get; init; }

    /// <summary>执行目标的名字（本机为机器名）。</summary>
    public required string HostName { get; init; }

    /// <summary>CLI 报出的上限重置时间；没报出来为 null。</summary>
    public DateTimeOffset? ResetsAt { get; init; }

    /// <summary>CLI 给用户看的那句话（如 "You've hit your session limit · resets 2:13am"）。</summary>
    public string? Notice { get; init; }
}
