using Serilog.Core;

namespace Tnzi.Logging.Tests.RequestLogging;

/// <summary>
/// Captures every emitted <see cref="LogEvent"/> so a test can assert on what
/// actually reached a sink - the rendered message *and* the structured
/// properties, since a credential can leak through either.
/// </summary>
public sealed class InMemorySink : ILogEventSink
{
    /// <summary>
    /// Serilog 给请求完成事件打的来源。Hosting 自己的诊断事件也带 <c>RequestPath</c>
    /// 属性，所以"找请求完成事件"必须按来源认，不能按属性名认。
    /// </summary>
    public const string SerilogRequestLoggingSource = "Serilog.AspNetCore.RequestLoggingMiddleware";

    private readonly List<LogEvent> _events = [];
    private readonly Lock _gate = new();

    public void Emit(LogEvent logEvent)
    {
        lock (_gate)
        {
            _events.Add(logEvent);
        }
    }

    public IReadOnlyList<LogEvent> Events
    {
        get
        {
            lock (_gate)
            {
                return [.. _events];
            }
        }
    }

    /// <summary>
    /// 只列出确实携带了给定明文的事件，并带上它们的 SourceContext -
    /// 断言失败时要能一眼看出是哪条日志把凭据写出去的。
    /// </summary>
    public IReadOnlyList<string> EventsContaining(string needle) =>
    [
        .. Events
            .Where(e => Describe(e).Contains(needle, StringComparison.Ordinal))
            .Select(Describe)
    ];

    /// <summary>
    /// 一条事件的全部可见面：级别、来源、渲染后的消息、每个属性值、异常。
    /// 凭据可以从其中任何一处漏出去，所以检索要覆盖全部。
    /// </summary>
    private static string Describe(LogEvent e)
    {
        var source = SourceOf(e) ?? "(no source)";
        var props = string.Join(", ", e.Properties.Select(p => $"{p.Key}={p.Value}"));
        return $"[{e.Level}] {source} :: {e.RenderMessage()} :: {props} :: {e.Exception}";
    }

    public static string? SourceOf(LogEvent e) =>
        e.Properties.TryGetValue("SourceContext", out var sc) ? sc.ToString().Trim('"') : null;

    /// <summary>
    /// Serilog 自己写的那条请求完成事件（每次请求恰好一条）。
    /// </summary>
    public LogEvent? RequestCompletionEvent() =>
        Events.SingleOrDefault(e => SourceOf(e) == SerilogRequestLoggingSource);
}
