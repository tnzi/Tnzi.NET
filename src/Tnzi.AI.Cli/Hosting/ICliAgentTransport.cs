namespace Tnzi.AI.Cli.Hosting;

/// <summary>
/// 子进程的<b>双向</b> stdio 通道。
/// </summary>
/// <remarks>
/// <para>
/// 双向是本模块与上一代实现的根本差异。旧契约是 <c>ParseOutputAsync(StreamReader stdout)</c> ——
/// 单向读，于是它从根上装不下两件真实存在的事：
/// </para>
/// <list type="bullet">
/// <item>stream-json 会在运行中发 <c>control_request</c> 要求应答，stdin 必须保持打开；</item>
/// <item>ACP 是完整的双向 JSON-RPC，agent 会反过来向客户端发 <c>session/request_permission</c>。</item>
/// </list>
/// <para>
/// 于是旧实现只能停在 prompt-in / text-out。
/// </para>
/// </remarks>
public interface ICliAgentTransport : IAsyncDisposable
{
    /// <summary>
    /// 逐行读取 stdout。
    /// </summary>
    /// <remarks>
    /// 实现<b>必须</b>用一个独立任务持续抽干 stdout 管道，而不是只在调用方迭代时才读 ——
    /// 否则大提示词写 stdin 时会与子进程写 stdout 互相阻塞（实测 1KB 侥幸通过、64KB 必死锁，
    /// 也就是说开发期用短提示根本测不出来，上生产遇到长上下文才炸）。
    /// </remarks>
    IAsyncEnumerable<string> ReadLinesAsync(CancellationToken cancellationToken);

    /// <summary>写入一行到 stdin 并 flush。<b>不关闭 stdin。</b></summary>
    Task WriteLineAsync(string line, CancellationToken cancellationToken);

    /// <summary>显式关闭 stdin（仅在协议要求 EOF 时调用）。</summary>
    Task CloseInputAsync();

    /// <summary>
    /// 进程崩溃时可读的 stderr 尾部（有界）。
    /// </summary>
    /// <remarks>
    /// 没有它，一次崩溃只剩下 <c>exit code 3</c>，无从定位。实测中正是 stderr 直接给出了
    /// 「resume 的会话不存在」这一根因。
    /// </remarks>
    string StderrTail { get; }
}
