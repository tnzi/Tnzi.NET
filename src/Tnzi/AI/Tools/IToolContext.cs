namespace Tnzi.AI.Tools;

/// <summary>
/// 工具执行上下文 — 提供工具运行时对 Scoped 服务的访问
/// </summary>
/// <remarks>
/// <para>
/// 工具提供者（IAIToolProvider）通常注册为 Singleton，无法通过构造函数注入 Scoped 服务
/// （如 ICurrentUser、ITenantProvider 等）。IToolContext 通过 AsyncLocal 机制在工具执行期间
/// 提供对当前请求作用域的 ServiceProvider 访问。
/// </para>
/// <para>
/// 使用方式：在工具方法中通过 <see cref="ToolContextAccessor.Current"/> 获取上下文：
/// <code>
/// var context = ToolContextAccessor.Current;
/// var currentUser = context?.GetService&lt;ICurrentUser&gt;();
/// </code>
/// </para>
/// </remarks>
[ExperimentalApi(Reason = "Tool context API may evolve based on usage patterns")]
public interface IToolContext
{
    /// <summary>
    /// 当前请求作用域的 ServiceProvider
    /// </summary>
    IServiceProvider RequestServices { get; }

    /// <summary>
    /// 当前请求的取消令牌
    /// </summary>
    CancellationToken CancellationToken { get; }

    /// <summary>
    /// 请求级别的键值存储（用于跨工具调用传递数据）
    /// </summary>
    IDictionary<string, object?> Items { get; }

    /// <summary>
    /// 从请求作用域获取服务实例
    /// </summary>
    T? GetService<T>() where T : class;

    /// <summary>
    /// 从请求作用域获取必需的服务实例
    /// </summary>
    /// <exception cref="InvalidOperationException">服务未注册</exception>
    T GetRequiredService<T>() where T : notnull;
}

/// <summary>
/// 工具上下文访问器 — 基于 AsyncLocal 的静态访问入口
/// </summary>
/// <remarks>
/// AgentRuntime 在执行 Agent 前设置 Current，执行完成后清除。
/// 工具提供者通过 <c>ToolContextAccessor.Current</c> 获取当前请求的上下文。
/// </remarks>
[ExperimentalApi(Reason = "Tool context API may evolve based on usage patterns")]
public static class ToolContextAccessor
{
    private static readonly AsyncLocal<IToolContext?> _current = new();

    /// <summary>
    /// 当前执行上下文（在工具执行期间可用，其他时候为 null）
    /// </summary>
    public static IToolContext? Current
    {
        get => _current.Value;
        set => _current.Value = value;
    }
}
