using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Tnzi.Payment.Subscriptions.Tests.Integration;

/// <summary>
/// 把服务经 <see cref="ILoggerFactory"/> 写出的日志原样记下来，供断言「这件事留下了告警」用。
/// </summary>
/// <remarks>
/// <c>ApplicationService.Logger</c> 取自 <see cref="ILoggerFactory"/>，所以在测试的服务集合里
/// 后注册一个它就能截到订阅服务自己的日志；基类注册的 <c>NullLoggerFactory</c> 被覆盖。
/// </remarks>
internal sealed class CapturingLoggerFactory : ILoggerFactory
{
    public ConcurrentQueue<(LogLevel Level, string Category, string Message)> Entries { get; } = new();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public void Dispose()
    {
    }

    /// <summary>是否写过一条不低于 <paramref name="level"/>、含指定片段的日志。</summary>
    public bool Logged(LogLevel level, string fragment)
        => Entries.Any(e => e.Level >= level && e.Message.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    private sealed class CapturingLogger(CapturingLoggerFactory owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => owner.Entries.Enqueue((logLevel, category, formatter(state, exception)));
    }
}
