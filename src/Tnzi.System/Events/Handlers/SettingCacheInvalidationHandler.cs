namespace Tnzi.System.Events.Handlers;

/// <summary>
/// 配置变更之后再清一次本实例的按键缓存（<c>Setting:{key}</c>，有效期 1 小时）。
/// </summary>
/// <remarks>
/// <para>
/// <c>SettingService</c> 在自己的工作单元结束后就地清一次缓存。但当它运行在外层环境工作单元里
/// （<c>AspNetCore:EnableGlobalUnitOfWork</c>，或调用方自己开的事务）时，内层只 flush 不提交：
/// 清缓存那一刻新值还没提交，并发的读取从库里读到旧值、再缓存整整一小时。
/// </para>
/// <para>
/// <see cref="SettingChangedEvent"/> 经事务感知发布在<b>提交之后</b>才派发，所以这里的第二次清除一定落在新值可见之后；
/// 没有外层事务时它紧跟写入执行，只是多清一次。与 reload / 实时广播那个处理器分开注册，因为那个只在
/// 接了配置源或 SignalR 时才注册，而缓存每个部署都有。
/// </para>
/// </remarks>
public class SettingCacheInvalidationHandler : IEventHandler<SettingChangedEvent>
{
    private readonly ICache _cache;

    public SettingCacheInvalidationHandler(ICache cache)
    {
        _cache = Check.NotNull(cache);
    }

    public async Task HandleAsync(SettingChangedEvent @event, CancellationToken cancellationToken = default)
    {
        Check.NotNull(@event);

        // 与 SettingService 的就地清除同一个键（任何作用域的写入它都清这个键）。
        await _cache.RemoveAsync($"Setting:{@event.Key}");

        // 主题键的增删还要清掉「已配置的主题键」清单（匿名主题读取据此跳过没配置的 scope）。
        if (AppearanceService.IsThemeSettingKey(@event.Key))
            await _cache.RemoveAsync(AppearanceService.ConfiguredThemeKeysCacheKey);
    }
}
