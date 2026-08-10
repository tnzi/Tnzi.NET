namespace Tnzi.Audit.Tests.TestSupport;

/// <summary>
/// 固定值的 <see cref="IOptionsMonitor{TOptions}"/>：被试服务热读选项，测试只需一个不变的值。
/// </summary>
/// <typeparam name="T">选项类型。</typeparam>
public sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
{
    /// <summary>用给定值初始化。</summary>
    /// <param name="value">选项值。</param>
    public StaticOptionsMonitor(T value) => CurrentValue = value;

    /// <inheritdoc />
    public T CurrentValue { get; }

    /// <inheritdoc />
    public T Get(string? name) => CurrentValue;

    /// <inheritdoc />
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
