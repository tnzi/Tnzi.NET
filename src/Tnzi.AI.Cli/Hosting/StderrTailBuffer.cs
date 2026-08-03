namespace Tnzi.AI.Cli.Hosting;

/// <summary>
/// 有界的 stderr 尾部缓冲：只保留最后 N 个字符。
/// </summary>
/// <remarks>
/// 为什么只留尾部：一个跑了三小时的 agent 可能往 stderr 写掉几百 MB 的进度噪音，
/// 而真正说明「为什么死了」的那几行永远在最后。全量保留是内存风险，只留头部则毫无价值。
/// </remarks>
public sealed class StderrTailBuffer
{
    /// <summary>默认保留的字符数。</summary>
    public const int DefaultCapacity = 8 * 1024;

    private readonly int _capacity;
    private readonly StringBuilder _buffer = new();
    private readonly Lock _gate = new();

    /// <summary>初始化尾部缓冲。</summary>
    public StderrTailBuffer(int capacity = DefaultCapacity)
    {
        _capacity = capacity > 0 ? capacity : DefaultCapacity;
    }

    /// <summary>追加一行。</summary>
    public void AppendLine(string? line)
    {
        if (line is null)
        {
            return;
        }

        lock (_gate)
        {
            _buffer.Append(line).Append('\n');
            var overflow = _buffer.Length - _capacity;
            if (overflow > 0)
            {
                _buffer.Remove(0, overflow);
            }
        }
    }

    /// <summary>取当前尾部内容。</summary>
    public string Tail()
    {
        lock (_gate)
        {
            return _buffer.ToString();
        }
    }
}
