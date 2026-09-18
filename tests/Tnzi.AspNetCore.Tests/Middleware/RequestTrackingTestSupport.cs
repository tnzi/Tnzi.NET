using Microsoft.Extensions.DependencyInjection;

namespace Tnzi.AspNetCore.Tests.Middleware;

/// <summary>直接调 <see cref="RequestTrackingMiddleware"/> 的用例共用的替身。</summary>
internal static class RequestTrackingTestSupport
{
    internal sealed class StaticMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = value;
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    /// <summary>捕获中间件记下的那一条 <see cref="RequestLogEntry"/>。</summary>
    internal sealed class CapturingLogger : ILogger<RequestTrackingMiddleware>
    {
        public List<RequestLogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (state is IReadOnlyList<KeyValuePair<string, object?>> values)
            {
                foreach (var pair in values)
                {
                    if (pair.Value is RequestLogEntry entry)
                    {
                        Entries.Add(entry);
                    }
                }
            }
        }
    }

    /// <summary>一个走 <c>/api/profile</c>（不在体采集排除名单里）的 POST 上下文。</summary>
    internal static DefaultHttpContext NewContext()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        // 刻意不走 /auth/ ——那条路径整条不采集体，会把用例变成永远绿。
        context.Request.Path = "/api/profile";
        context.RequestServices = new ServiceCollection().BuildServiceProvider();
        context.Response.Body = new MemoryStream();
        return context;
    }

    internal static RequestTrackingMiddleware NewMiddleware(
        RequestDelegate next, CapturingLogger logger, RequestTrackingOptions options)
        => new(
            next,
            logger,
            new StaticMonitor<AspNetCoreOptions>(new AspNetCoreOptions()),
            new StaticMonitor<RequestTrackingOptions>(options));
}
