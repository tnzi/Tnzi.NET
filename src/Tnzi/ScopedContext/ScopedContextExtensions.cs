
namespace Tnzi.ScopedContext;

/// <summary>
/// 作用域上下文扩展方法
/// </summary>
public static class ScopedContextExtensions
{
    /// <summary>
    /// 获取作用域上下文（从服务提供者）
    /// </summary>
    public static IScopedContext GetScopedContext(this IServiceProvider serviceProvider)
        => serviceProvider.GetRequiredService<IScopedContext>();

    /// <summary>
    /// 获取作用域上下文（从 HttpContext）
    /// </summary>
    public static IScopedContext GetScopedContext(this HttpContext httpContext)
        => httpContext.RequestServices.GetRequiredService<IScopedContext>();

    /// <summary>
    /// 在作用域中执行操作
    /// </summary>
    public static async Task ExecuteInScopeAsync(
        this IServiceScopeFactory scopeFactory,
        Func<IScopedContext, Task> action)
    {
        using var scope = scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IScopedContext>();
        await action(context);
    }

    /// <summary>
    /// 在作用域中执行操作并返回结果
    /// </summary>
    public static async Task<TResult> ExecuteInScopeAsync<TResult>(
        this IServiceScopeFactory scopeFactory,
        Func<IScopedContext, Task<TResult>> func)
    {
        using var scope = scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IScopedContext>();
        return await func(context);
    }
}