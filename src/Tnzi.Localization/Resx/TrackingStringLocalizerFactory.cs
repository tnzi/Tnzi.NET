namespace Tnzi.Localization.Resx;

/// <summary>
/// 装饰 Resx 模式的 <see cref="IStringLocalizerFactory"/>（ASP.NET Core 自带的 <c>ResourceManagerStringLocalizerFactory</c>），
/// 让它交出的每个 <see cref="IStringLocalizer"/> 在未命中时把键报给 <see cref="IMissingTranslationTracker"/>。
/// </summary>
/// <remarks>
/// ★ 此前 <c>TrackMissing</c> 全仓唯一的调用点在 <see cref="JsonStringLocalizer"/>，而 <c>ResourceFormat</c> 默认是 Resx：
/// 追踪器无条件注册、管理端点与权限码无条件在场，默认部署上「Missing Translations」页却永远是空的 ——
/// 与「所有翻译都齐了」逐字相同。Resx 的查找与回退仍全部交给 <c>ResourceManager</c>（当前文化 → 父链 → 中性资源），
/// 本类只在 <see cref="LocalizedString.ResourceNotFound"/> 为 true 时记一笔，命中中性资源的键不算缺失。
/// </remarks>
public sealed class TrackingStringLocalizerFactory : IStringLocalizerFactory
{
    private readonly IStringLocalizerFactory _inner;
    private readonly IMissingTranslationTracker _tracker;

    public TrackingStringLocalizerFactory(IStringLocalizerFactory inner, IMissingTranslationTracker tracker)
    {
        _inner = Check.NotNull(inner);
        _tracker = Check.NotNull(tracker);
    }

    /// <inheritdoc />
    public IStringLocalizer Create(Type resourceSource)
    {
        return new TrackingStringLocalizer(_inner.Create(resourceSource), _tracker);
    }

    /// <inheritdoc />
    public IStringLocalizer Create(string baseName, string location)
    {
        return new TrackingStringLocalizer(_inner.Create(baseName, location), _tracker);
    }

    /// <summary>
    /// 索引器未命中即追踪，其余成员透传
    /// </summary>
    private sealed class TrackingStringLocalizer : IStringLocalizer
    {
        private readonly IStringLocalizer _inner;
        private readonly IMissingTranslationTracker _tracker;

        public TrackingStringLocalizer(IStringLocalizer inner, IMissingTranslationTracker tracker)
        {
            _inner = inner;
            _tracker = tracker;
        }

        public LocalizedString this[string name] => Track(_inner[name]);

        public LocalizedString this[string name, params object[] arguments] => Track(_inner[name, arguments]);

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
        {
            return _inner.GetAllStrings(includeParentCultures);
        }

        private LocalizedString Track(LocalizedString result)
        {
            if (result.ResourceNotFound)
            {
                _tracker.TrackMissing(CultureInfo.CurrentUICulture.Name, result.Name);
            }

            return result;
        }
    }
}
