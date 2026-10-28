using Tnzi.System.Events;
using Tnzi.System.Events.Handlers;

namespace Tnzi.System.Tests.Events;

/// <summary>
/// 配置变更事件在提交之后才派发：<c>SettingService</c> 的就地清除在外层事务提交之前，
/// 并发读取会把旧值再缓存一小时，所以事件到达时要再清一次同一个键。
/// </summary>
public class SettingCacheInvalidationHandlerTests
{
    [Theory]
    [InlineData(SettingScope.Global, null)]
    [InlineData(SettingScope.Tenant, "t1")]
    public async Task Change_ClearsTheSettingCacheKey(SettingScope scope, string? scopeId)
    {
        var cache = new Mock<ICache>();
        var handler = new SettingCacheInvalidationHandler(cache.Object);

        await handler.HandleAsync(new SettingChangedEvent { Key = "Chat:AllowInvisible", Scope = scope, ScopeId = scopeId });

        cache.Verify(c => c.RemoveAsync("Setting:Chat:AllowInvisible"), Times.Once);
        cache.Verify(c => c.RemoveAsync(AppearanceService.ConfiguredThemeKeysCacheKey), Times.Never);
    }

    /// <summary>主题键的增删还要清掉「已配置的主题键」清单：匿名主题读取据此跳过没配置的 scope，新保存的 scope 不能被它挡住。</summary>
    [Theory]
    [InlineData("Appearance:Theme:chat")]
    [InlineData("Appearance:AdminTheme")]
    public async Task A_theme_change_also_clears_the_configured_theme_scopes(string key)
    {
        var cache = new Mock<ICache>();
        var handler = new SettingCacheInvalidationHandler(cache.Object);

        await handler.HandleAsync(new SettingChangedEvent { Key = key, Scope = SettingScope.Global });

        cache.Verify(c => c.RemoveAsync($"Setting:{key}"), Times.Once);
        cache.Verify(c => c.RemoveAsync(AppearanceService.ConfiguredThemeKeysCacheKey), Times.Once);
    }
}
