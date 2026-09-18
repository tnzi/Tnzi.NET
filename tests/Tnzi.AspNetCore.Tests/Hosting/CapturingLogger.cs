namespace Tnzi.AspNetCore.Tests.Hosting;

/// <summary>
/// 把日志条目原样记下来的 <see cref="ILogger{T}"/>，供断言「某条 sent 日志没有被写出」用。
/// </summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => Entries.Add((logLevel, formatter(state, exception)));

    /// <summary>是否写过一条含指定片段的 Information 日志。</summary>
    public bool LoggedInformationContaining(string fragment)
        => Entries.Any(e => e.Level == LogLevel.Information && e.Message.Contains(fragment, StringComparison.OrdinalIgnoreCase));
}
