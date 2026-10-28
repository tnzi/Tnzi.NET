namespace Tnzi.AI.Cli.Hosting;

/// <summary>
/// 接回一个进程要的东西：哪条运行、宿主当初交出的句柄。
/// </summary>
[ExperimentalApi(Reason = "Reattaching to processes that outlive a server restart is new")]
public sealed record CliReattachRequest
{
    /// <summary>运行。</summary>
    public required Guid RunId { get; init; }

    /// <summary>宿主起进程时交出的句柄（<see cref="ICliProcess.ReattachHandle"/>），原样交回。</summary>
    public required string Handle { get; init; }

    /// <summary>接回来以后要终止它时的宽限期（同 <see cref="CliProcessSpec.TerminateGrace"/>）。</summary>
    public TimeSpan TerminateGrace { get; init; } = TimeSpan.FromSeconds(5);
}

/// <summary>
/// <see cref="ICliHostTarget.ReattachAsync"/> 的回答：接回来了，或者进程已经不在。
/// </summary>
/// <remarks>眼下连不上不是回答：抛 <see cref="CliHostUnavailableException"/>，框架稍后再问。</remarks>
[ExperimentalApi(Reason = "Reattaching to processes that outlive a server restart is new")]
public sealed record CliReattachResult
{
    private CliReattachResult(ICliReattachedProcess? process, string? goneReason)
    {
        Process = process;
        GoneReason = goneReason;
    }

    /// <summary>接回来的进程；进程不在时为 null。</summary>
    public ICliReattachedProcess? Process { get; }

    /// <summary>进程为什么不在（进运行记录与给 CLI 的下一轮说明）；接回来时为 null。</summary>
    public string? GoneReason { get; }

    /// <summary>进程还在，或者已经退出但输出都还在。</summary>
    public static CliReattachResult Attached(ICliReattachedProcess process) => new(Check.NotNull(process), null);

    /// <summary>进程不在了，输出也取不回来（被杀掉、容器重启、超出了宿主的保留上限）。</summary>
    /// <param name="reason">给使用者看的英文原因，例如 <c>"The workspace container was restarted after running out of memory."</c></param>
    public static CliReattachResult Gone(string reason) => new(null, Check.NotNullOrWhiteSpace(reason));
}

/// <summary>
/// 接回来的进程：除了照常的终止、等待与 stdin 写入，还能从第一行起重放它的输出。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么从第一行重放，而不是从上次确认的地方接着读</b>：协议适配器是有状态的。stream-json 适配器边读边累计用量、
/// 会话 id、限流状态与最后一条回复；ACP 适配器靠 JSON-RPC 请求 id 认出 <c>session/prompt</c> 的应答，那才是这一轮结束的信号。
/// 服务端重启后这些状态都没了，只从中间接着读，新的适配器认不出那条应答，用量与结局也是错的。从第一行重放，适配器按原来的次序
/// 把同样的输入再看一遍，状态就与停机前一样；已经落库的事件按序号跳过，不会写第二遍。
/// </para>
/// <para>
/// 宿主因此要从进程启动起保留全部 stdout（可以落盘），而不是在服务端确认之后丢掉。超出保留上限、取不回开头那部分时，
/// 回答 <see cref="CliReattachResult.Gone"/> 而不是只给后半段。
/// </para>
/// <para>
/// stdin 写入：<see cref="ICliProcess.Transport"/> 的 <see cref="ICliAgentTransport.WriteLineAsync"/> 照常写到进程的 stdin。
/// 重放时适配器会把上一次写过的那些行再写一遍（初始化请求、提示词、权限应答），框架按 <see cref="StdinLinesReceived"/>
/// 丢掉前面这么多行，只把新的那些交给宿主，所以进程不会收到重复的输入。读输出只走 <see cref="ReadOutputAsync"/>，
/// 框架不调 transport 的 <see cref="ICliAgentTransport.ReadLinesAsync"/>。
/// </para>
/// </remarks>
[ExperimentalApi(Reason = "Reattaching to processes that outlive a server restart is new")]
public interface ICliReattachedProcess : ICliProcess
{
    /// <summary>进程从启动到现在一共收到了几行 stdin（跨所有连接，只算完整收到的行）。</summary>
    long StdinLinesReceived { get; }

    /// <summary>
    /// 从进程写出的第一行 stdout 起按顺序给出每一行，给完已有的再接着给实时的；进程退出、输出读完时结束。
    /// </summary>
    IAsyncEnumerable<CliOutputLine> ReadOutputAsync(CancellationToken cancellationToken);
}

/// <summary>
/// 进程写出的一行 stdout，以及它写这一行之前一共收到了几行 stdin。
/// </summary>
/// <param name="Text">这一行（不含换行符）。</param>
/// <param name="StdinLinesBefore">
/// 宿主读到这一行时，进程已经收到的 stdin 行数。重放靠它保持原来的先后次序：一行要等适配器重新发出了这么多行 stdin 之后
/// 才交给它，否则 ACP 的应答可能比它对应的请求先到、被当成没人等的应答丢掉。
/// </param>
public readonly record struct CliOutputLine(string Text, long StdinLinesBefore);
