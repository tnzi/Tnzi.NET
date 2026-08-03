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
    public async Task CloseInputAsync()
    {
        if (Interlocked.Exchange(ref _stdinClosed, 1) != 0)
        {
            return;
        }

        await _writeGate.WaitAsync();
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

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await CloseInputAsync();
        _stdout.Writer.TryComplete();
        _writeGate.Dispose();
    }
}
