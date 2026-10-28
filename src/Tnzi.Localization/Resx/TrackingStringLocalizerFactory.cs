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
/// <para>
/// ★★ 一个资源都没有的资源源（任何受支持语言的整条回退链上都没有清单）不记：Resx 只在资源类型自己的程序集里找，
/// 这种源的键在这个部署里无处可补，记下来只会把页面堆满运维无论如何也处理不了的条目。
/// 典型是框架的 <see cref="SharedResource"/>：它所在的程序集没有任何 .resx，而异常处理每次都经它查错误码，
/// 此前 NOT_FOUND、VALIDATION_ERROR 之类的键因此全部被记成缺失。判定按资源源只做一次并缓存。
/// </para>
/// </remarks>
public sealed class TrackingStringLocalizerFactory : IStringLocalizerFactory
{
    private readonly IStringLocalizerFactory _inner;
    private readonly IMissingTranslationTracker _tracker;
    private readonly IOptions<RequestLocalizationOptions>? _requestLocalization;
    private readonly ConcurrentDictionary<string, Lazy<bool>> _hasResources = new(StringComparer.Ordinal);

    public TrackingStringLocalizerFactory(
        IStringLocalizerFactory inner,
        IMissingTranslationTracker tracker,
        IOptions<RequestLocalizationOptions>? requestLocalization = null)
    {
        _inner = Check.NotNull(inner);
        _tracker = Check.NotNull(tracker);
        _requestLocalization = requestLocalization;
    }

    /// <inheritdoc />
    public IStringLocalizer Create(Type resourceSource)
    {
        Check.NotNull(resourceSource);
        var inner = _inner.Create(resourceSource);
        return new TrackingStringLocalizer(inner, _tracker, () => HasResources($"type:{resourceSource.AssemblyQualifiedName}", inner));
    }

    /// <inheritdoc />
    public IStringLocalizer Create(string baseName, string location)
    {
        var inner = _inner.Create(baseName, location);
        return new TrackingStringLocalizer(inner, _tracker, () => HasResources($"name:{baseName}|{location}", inner));
    }

    /// <summary>
    /// 这个资源源在任一受支持的界面语言（连同父链与中性资源）下有没有至少一条资源。
    /// </summary>
    private bool HasResources(string sourceKey, IStringLocalizer inner)
        => _hasResources.GetOrAdd(sourceKey, _ => new Lazy<bool>(() => ProbeResources(inner))).Value;

    private bool ProbeResources(IStringLocalizer inner)
    {
        var cultures = _requestLocalization?.Value.SupportedUICultures is { Count: > 0 } supported
            ? supported
            : [CultureInfo.CurrentUICulture];

        var previous = CultureInfo.CurrentUICulture;
        try
        {
            foreach (var culture in cultures)
            {
                CultureInfo.CurrentUICulture = culture;
                try
                {
                    if (inner.GetAllStrings(includeParentCultures: true).Any())
                    {
                        return true;
                    }
                }
                catch (MissingManifestResourceException)
                {
                    // 这个语言的整条回退链上没有清单，看下一个
                }
            }

            return false;
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    /// <summary>
    /// 索引器未命中即追踪（资源源本身有资源时），其余成员透传
    /// </summary>
    private sealed class TrackingStringLocalizer : IStringLocalizer
    {
        private readonly IStringLocalizer _inner;
        private readonly IMissingTranslationTracker _tracker;
        private readonly Func<bool> _sourceHasResources;

        public TrackingStringLocalizer(IStringLocalizer inner, IMissingTranslationTracker tracker, Func<bool> sourceHasResources)
        {
            _inner = inner;
            _tracker = tracker;
            _sourceHasResources = sourceHasResources;
        }

        public LocalizedString this[string name] => Track(_inner[name]);

        public LocalizedString this[string name, params object[] arguments] => Track(_inner[name, arguments]);

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
        {
            return _inner.GetAllStrings(includeParentCultures);
        }

        private LocalizedString Track(LocalizedString result)
        {
            if (result.ResourceNotFound && _sourceHasResources())
            {
                _tracker.TrackMissing(CultureInfo.CurrentUICulture.Name, result.Name);
            }

            return result;
        }
    }
}
