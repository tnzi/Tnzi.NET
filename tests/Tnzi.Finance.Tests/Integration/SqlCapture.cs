using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// 只收 EF 的 Database.Command 日志（含 EnableSensitiveDataLogging 打印的参数值）
/// </summary>
internal sealed class SqlCapture : ILoggerFactory
{
    private readonly List<string> _commands = [];

    public IReadOnlyList<string> Commands
    {
        get { lock (_commands) return [.. _commands]; }
    }

    public void Clear()
    {
        lock (_commands) _commands.Clear();
    }

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public ILogger CreateLogger(string categoryName) => new CategoryLogger(this, categoryName);

    public void Dispose()
    {
    }

    private sealed class CategoryLogger(SqlCapture owner, string category) : ILogger
    {
        private readonly bool _isCommand = category == DbLoggerCategory.Database.Command.Name;

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => _isCommand;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!_isCommand)
                return;

            var message = formatter(state, exception);
            lock (owner._commands)
                owner._commands.Add(message);
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
