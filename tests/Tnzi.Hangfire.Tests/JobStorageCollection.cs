namespace Tnzi.Hangfire.Tests;

/// <summary>
/// 触碰进程级静态 <c>JobStorage.Current</c> 的测试类共用的集合：xUnit 对同一集合内的类串行执行。
/// 置空它的类与经静态门面入队的类并行时，后者会偶发 "JobStorage.Current property value has not been initialized"。
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class JobStorageCollection
{
    public const string Name = "JobStorage";
}
