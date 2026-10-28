namespace Tnzi.AI.Cli.Hosting;

/// <summary>
/// 把一个接回来的进程包成适配器认得的 <see cref="ICliProcess"/>：输出从第一行起重放、按原来的先后次序交出，
/// 重放中重新发出的、进程早已收到过的 stdin 行不再发给宿主。
/// </summary>
internal sealed class CliReplayProcess : ICliProcess
{
    private readonly ICliReattachedProcess _inner;
    private readonly ReplayTransport _transport;

    public CliReplayProcess(ICliReattachedProcess inner, TimeSpan orderingWait, ILogger logger)
    {
        _inner = Check.NotNull(inner);
        _transport = new ReplayTransport(inner, orderingWait, Check.NotNull(logger));
    }

    public ICliAgentTransport Transport => _transport;

    public int ProcessId => _inner.ProcessId;

    public bool HasExited => _inner.HasExited;

    public int? ExitCode => _inner.ExitCode;

    public string? ReattachHandle => _inner.ReattachHandle;

    public Task TerminateAsync(CancellationToken cancellationToken) => _inner.TerminateAsync(cancellationToken);

    public Task<int> WaitForExitAsync(CancellationToken cancellationToken) => _inner.WaitForExitAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        _transport.Dispose();
        await _inner.DisposeAsync();
    }

    /// <summary>
    /// 重放的 transport。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 写：适配器每写一行都计数；前 <see cref="ICliReattachedProcess.StdinLinesReceived"/> 行是上一次已经送到进程的那些，丢掉，
    /// 之后的照常写给宿主。计数在调用的那一刻同步完成（stream-json 的权限应答是发出去不等的写），所以按调用次序就是按写入次序。
    /// </para>
    /// <para>
    /// 读：一行要等适配器已经写出了进程写它之前收到的那么多行 stdin 才交出。没有这一步，读循环会跑在生命周期之前：
    /// ACP 的应答比它对应的请求先到，被当成没人等的应答丢掉；读循环先应答了一个权限请求，那一行顶掉了一行本该丢掉的旧写入，
    /// 进程于是收到一份重复的 <c>session/prompt</c>，而那个权限请求再也没人应答。
    /// </para>
    /// </remarks>
    private sealed class ReplayTransport(ICliReattachedProcess process, TimeSpan orderingWait, ILogger logger)
        : ICliAgentTransport, IDisposable
    {
        private readonly long _alreadyReceived = process.StdinLinesReceived;
        private readonly SemaphoreSlim _written = new(0);
        private long _writes;

        public string StderrTail => process.Transport.StderrTail;

        public async IAsyncEnumerable<string> ReadLinesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var line in process.ReadOutputAsync(cancellationToken))
            {
                await WaitForWritesAsync(line.StdinLinesBefore, cancellationToken);
                yield return line.Text;
            }
        }

        public Task WriteLineAsync(string line, CancellationToken cancellationToken)
        {
            var issued = Interlocked.Increment(ref _writes);
            _written.Release();

            // 进程早就收到过这一行（上一次执行写的），不再发一次
            return issued <= _alreadyReceived
                ? Task.CompletedTask
                : process.Transport.WriteLineAsync(line, cancellationToken);
        }

        public Task CloseInputAsync() => process.Transport.CloseInputAsync();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void Dispose() => _written.Dispose();

        private async Task WaitForWritesAsync(long needed, CancellationToken cancellationToken)
        {
            if (Interlocked.Read(ref _writes) >= needed)
            {
                return;
            }

            var deadline = DateTime.UtcNow + orderingWait;
            while (Interlocked.Read(ref _writes) < needed)
            {
                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero || !await _written.WaitAsync(remaining, cancellationToken))
                {
                    // 这一次的适配器没有发出上一次那么多写入（绑定在中途改过，或者 CLI 的行为与上一次不同）：照常往下读，
                    // 停在这里只会让整条运行等到空闲看门狗
                    logger.LogWarning(
                        "Replaying a reattached CLI process: the adapter wrote {Written} of the {Needed} stdin line(s) the process had " +
                        "received before this output line; continuing without them",
                        Interlocked.Read(ref _writes), needed);
                    return;
                }
            }
        }
    }
}
