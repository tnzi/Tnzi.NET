using Microsoft.Extensions.Options;

namespace Tnzi.AI.Tests;

/// <summary>
/// 固定值 options 桩 — 供单元测试注入已切换到 IOptionsMonitor / IOptionsSnapshot / IOptions 的消费者。
/// 三接口统一返回构造时传入的值；OnChange 不触发（返回 null）。
/// </summary>
internal sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>, IOptionsSnapshot<T>
    where T : class
{
    public T CurrentValue { get; } = value;

    public T Value => CurrentValue;

    public T Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
