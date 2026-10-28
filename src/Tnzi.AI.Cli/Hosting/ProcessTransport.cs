namespace Tnzi.AI.Cli.Hosting;

/// <summary>
/// 基于 <see cref="Process"/> 重定向管道的双向 stdio 通道。
/// </summary>
/// <remarks>
/// <para>
/// <b>构造时立即启动 stdout 抽水任务</b>，而不是等调用方开始迭代。这不是优化而是正确性要求：
/// 写一个 64KB 的提示到 stdin 时，子进程的 stdin 管道缓冲区会写满；此时若没有人在读 stdout，
/// 子进程也会阻塞在写 stdout 上 —— 双方各自等对方，死锁。实测 1KB 的提示能侥幸通过
/// （塞得进管道缓冲区，写调用立刻返回），64KB 必死。也就是说<b>开发期用短提示根本测不出来</b>。
/// </para>
/// <para>
/// stdout 用<b>无界</b> channel：有界 channel 在消费者慢时会让抽水任务阻塞，
/// 于是又回到「没人抽干管道」的死锁前提上。事件消费者（协议适配器）是纯 CPU 解析，
/// 不会长期落后。
/// </para>
/// </remarks>
public sealed class ProcessTransport : ICliAgentTransport
{
    /// <summary>释放时最多等多久让一次正在进行的写入让出写通道（理由见 <see cref="DisposeAsync"/>）。</summary>
    internal static readonly TimeSpan DisposeWriteTimeout = TimeSpan.FromSeconds(2);

    private readonly Process _process;
    private readonly Channel<string> _stdout = Channel.CreateUnbounded<string>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
    private readonly StderrTailBuffer _stderrTail;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly Task _stdoutPump;
    private readonly Task _stderrPump;
    private readonly ILogger _logger;
    private readonly string _providerKey;
    private int _stdinClosed;

    /// <summary>初始化并立即开始抽干两条输出管道。</summary>
    public ProcessTransport(Process process, string providerKey, ILogger logger, int stderrTailCapacity = StderrTailBuffer.DefaultCapacity)
    {
        _process = Check.NotNull(process);
        _providerKey = Check.NotNullOrWhiteSpace(providerKey);
        _logger = Check.NotNull(logger);
        _stderrTail = new StderrTailBuffer(stderrTailCapacity);

        _stdoutPump = Task.Run(PumpStdoutAsync);
        _stderrPump = Task.Run(PumpStderrAsync);
    }

    /// <inheritdoc />
    public string StderrTail => _stderrTail.Tail();

    /// <inheritdoc />
    public IAsyncEnumerable<string> ReadLinesAsync(CancellationToken cancellationToken)
        => _stdout.Reader.ReadAllAsync(cancellationToken);

    /// <inheritdoc />
    public async Task WriteLineAsync(string line, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _stdinClosed) != 0)
        {
            return;
        }

        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await _process.StandardInput.WriteLineAsync(line.AsMemory(), cancellationToken);
            await _process.StandardInput.FlushAsync(cancellationToken);
        }
        catch (IOException ex)
        {
            // 管道断开 = 子进程已退出。这是终止路径上的常态，不是故障：
            // 真正的失败原因由 stderr 尾部和退出码给出，这里再抛一次只会掩盖它。
            _logger.LogDebug(ex, "[{Provider}] stdin write failed; the process has likely exited", _providerKey);
            Volatile.Write(ref _stdinClosed, 1);
        }
        catch (ObjectDisposedException)
        {
            Volatile.Write(ref _stdinClosed, 1);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    /// <inheritdoc />
    public Task CloseInputAsync() => TryCloseInputAsync(Timeout.InfiniteTimeSpan);

    /// <summary>
    /// 关闭 stdin，但最多等 <paramref name="writeTimeout"/> 让正在进行的写入让出写通道。
    /// </summary>
    /// <returns>
    /// stdin 已关闭（或此前已被关闭、已断开）时为 true；写通道在时限内没让出来时为 false，
    /// 此时 stdin 仍被标记为关闭，之后的写入一律直接返回。
    /// </returns>
    /// <remarks>
    /// 终止流程用它而不是无限期地等：子进程不读 stdin 时（卡死的 CLI 正是这样），一次大写入会
    /// 一直卡在管道上、占着写通道。无限期地等它，终止就永远走不到强制杀进程那一档，而杀掉进程
    /// 恰恰是让那次写入（以管道断开）结束的唯一办法。
    /// </remarks>
    internal async Task<bool> TryCloseInputAsync(TimeSpan writeTimeout)
    {
        if (Interlocked.Exchange(ref _stdinClosed, 1) != 0)
        {
            return true;
        }

        if (!await _writeGate.WaitAsync(writeTimeout))
        {
            _logger.LogDebug("[{Provider}] stdin left open: a write is still blocked on the pipe", _providerKey);
            return false;
        }

        try
        {
            _process.StandardInput.Close();
        }
        catch (IOException)
        {
            // 已经断开，无事可做。
        }
        catch (ObjectDisposedException)
        {
            // 同上。
        }
        finally
        {
            _writeGate.Release();
        }

        return true;
    }

    private async Task PumpStdoutAsync()
    {
        try
        {
            while (await _process.StandardOutput.ReadLineAsync() is { } line)
            {
                await _stdout.Writer.WriteAsync(line);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // 进程被终止时管道会被拆掉，这是预期路径。
            _logger.LogDebug(ex, "[{Provider}] stdout pump ended early", _providerKey);
        }
        finally
        {
            _stdout.Writer.TryComplete();
        }
    }

    private async Task PumpStderrAsync()
    {
        try
        {
            while (await _process.StandardError.ReadLineAsync() is { } line)
            {
                _stderrTail.AppendLine(line);
                _logger.LogDebug("[{Provider}:stderr] {Line}", _providerKey, line);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            _logger.LogDebug(ex, "[{Provider}] stderr pump ended early", _providerKey);
        }
    }

    /// <summary>
    /// 等待两条抽水任务收尾。终止流程在杀掉进程<b>之后</b>调用它，
    /// 以保证 <see cref="StderrTail"/> 已经包含子进程写出的最后几行。
    /// </summary>
    internal async Task DrainAsync(TimeSpan timeout)
    {
        var pumps = Task.WhenAll(_stdoutPump, _stderrPump);
        var completed = await Task.WhenAny(pumps, Task.Delay(timeout));
        if (completed == pumps)
        {
            // 观察异常，避免 TaskScheduler.UnobservedTaskException。
            await pumps;
        }
    }

    /// <summary>
    /// 关 stdin（有上限地等写通道）并结束 stdout 通道。
    /// </summary>
    /// <remarks>
    /// 不能无限期地等写通道：子进程不读 stdin 时，一次卡在管道上的写入会一直占着它，而那次写入只会在进程被杀、
    /// 管道断开时收尾，在那之前释放永远返回不了，持有它的调用方也就跟着挂住。等不到就放弃关 stdin
    ///（它已被标记为关闭，之后的写入直接返回），与终止流程同一处理。
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        await TryCloseInputAsync(DisposeWriteTimeout);
        _stdout.Writer.TryComplete();

        // ★写通道刻意不释放。终止流程放弃关 stdin 时（见 TryCloseInputAsync），那次卡住的写入要等
        // 进程被杀、管道断开才收尾，可能晚于这里；它收尾时要归还写通道，释放过的信号量会在那一刻
        // 抛 ObjectDisposedException。SemaphoreSlim 只有访问过 AvailableWaitHandle 才持有内核对象，
        // 这里从不访问，不释放也没有任何泄漏。
    }
}
