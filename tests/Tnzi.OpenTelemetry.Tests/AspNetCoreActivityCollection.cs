namespace Tnzi.OpenTelemetry.Tests;

/// <summary>
/// 断言「捕获到的服务端 span」的测试放在这个集合里，单独跑、不与其它集合并行。
/// </summary>
/// <remarks>
/// ASP.NET Core 的请求活动源是进程级的：任何一个测试宿主上的请求都会被这里注册的追踪处理器收到。
/// 与起了宿主的测试类（例如 OTLP 采集端）并行时，处理器里会多出别人的 span。
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AspNetCoreActivityCollection
{
    public const string Name = "ASP.NET Core activity source";
}
