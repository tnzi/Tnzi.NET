namespace Tnzi.Logging.Tests.RequestLogging;

/// <summary>
/// Serilog 的 <see cref="Serilog.Log.Logger"/> 是进程级静态。凡是替换它的测试类
/// 都要进这个集合，否则 xunit 会并行跑它们，一个类换走 logger 会让另一个类的
/// 事件落进别人的 sink（表现为随机的空断言，不是稳定失败）。
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public class SerilogGlobalCollection
{
    public const string Name = "Serilog global logger";
}
