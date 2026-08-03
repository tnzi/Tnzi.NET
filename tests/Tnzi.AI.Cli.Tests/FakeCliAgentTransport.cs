namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// 用录制好的协议帧喂适配器的假 transport。
/// </summary>
/// <remarks>
/// <b>硬规矩：默认测试绝不解析或执行用户安装的任何 agent CLI。</b>
/// CI 机器上可能真的装了 claude —— 一个手滑的测试会真的调用账号、消耗配额。
/// 所有适配器测试一律走这个假实现或测试自建的脚本，真机冒烟测试单独打标签、默认过滤掉。
/// </remarks>
internal sealed class FakeCliAgentTransport : ICliAgentTransport
{
    private readonly Channel<string> _lines = Channel.CreateUnbounded<string>();
    private readonly List<string> _written = [];
    private readonly Func<string, IEnumerable<string>>? _onWrite;

    /// <param name="scriptedLines">按顺序发给适配器的 stdout 行。</param>
    /// <param name="stderrTail">模拟的 stderr 尾部。</param>
    /// <param name="onWrite">
    /// 收到一行 stdin 写入时追加的 stdout 行。用来模拟<b>双向</b>协议
    /// （ACP 的请求/响应、stream-json 的 control_request 应答）。
    /// </param>
    public FakeCliAgentTransport(
        IEnumerable<string> scriptedLines,
        string stderrTail = "",
        Func<string, IEnumerable<string>>? onWrite = null)
    {
        StderrTail = stderrTail;
        _onWrite = onWrite;

        foreach (var line in scriptedLines)
        {
            _lines.Writer.TryWrite(line);
        }

        if (onWrite is null)
        {
            _lines.Writer.TryComplete();
        }
    }

    /// <summary>适配器写到 stdin 的全部内容。</summary>
    public IReadOnlyList<string> Written => _written;

    /// <summary>stdin 是否已被显式关闭。</summary>
    public bool InputClosed { get; private set; }

    /// <inheritdoc />
    public string StderrTail { get; }

    /// <inheritdoc />
    public IAsyncEnumerable<string> ReadLinesAsync(CancellationToken cancellationToken)
        => _lines.Reader.ReadAllAsync(cancellationToken);

    /// <inheritdoc />
    public Task WriteLineAsync(string line, CancellationToken cancellationToken)
    {
        _written.Add(line);

        if (_onWrite is not null)
        {
            foreach (var response in _onWrite(line))
            {
                _lines.Writer.TryWrite(response);
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task CloseInputAsync()
    {
        InputClosed = true;
        _lines.Writer.TryComplete();
        return Task.CompletedTask;
    }

    /// <summary>测试主动结束 stdout 流。</summary>
    public void CompleteOutput() => _lines.Writer.TryComplete();

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _lines.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}
