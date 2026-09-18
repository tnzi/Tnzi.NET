namespace Tnzi.Notification.Services;

/// <summary>
/// <see cref="INotificationProviderSelector"/> 的默认实现：什么都不选，一切走默认发送器。
/// </summary>
/// <remarks>
/// 消费方注册自己的实现即可替换（普通注册赢过模块里的 <c>TryAdd</c>）。
/// </remarks>
public sealed class DefaultNotificationProviderSelector : INotificationProviderSelector
{
    /// <inheritdoc />
    public ValueTask<string?> SelectAsync(NotificationProviderSelectionContext context, CancellationToken cancellationToken = default)
        => ValueTask.FromResult<string?>(null);
}
