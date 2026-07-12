namespace Tnzi.Storage.Tests.TestSupport;

/// <summary>
/// 固定值的 <see cref="IOptionsMonitor{T}"/> 测试桩：CurrentValue/Get 恒返回构造时传入的实例。
/// 持有引用（非拷贝），因此对底层 options 对象字段的后续修改可经 CurrentValue 观察到，
/// 与此前 Options.Create(...) 的行为一致。
/// </summary>
internal sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue => value;

    public T Get(string? name) => value;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
