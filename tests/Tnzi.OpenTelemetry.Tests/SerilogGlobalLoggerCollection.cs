namespace Tnzi.OpenTelemetry.Tests;

/// <summary>
/// 跑 <c>LoggingModule</c> 的测试共用这个集合，彼此串行。
/// </summary>
/// <remarks>
/// <c>LoggingModule</c> 把进程级的 Serilog <c>Log.Logger</c> 换成自己的，测试在 <c>Dispose</c> 里
/// <c>Log.CloseAndFlush()</c> 再还原。两个这样的测试类并行时，一个的清理会关掉另一个正在用的管线：
/// 日志在半路被丢掉，读起来像日志导出没接上。
/// </remarks>
[CollectionDefinition(Name)]
public sealed class SerilogGlobalLoggerCollection
{
    public const string Name = "Serilog global logger";
}
