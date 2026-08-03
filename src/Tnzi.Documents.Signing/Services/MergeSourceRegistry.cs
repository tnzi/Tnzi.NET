namespace Tnzi.Documents.Signing.Services;

/// <inheritdoc cref="IMergeSourceRegistry" />
/// <remarks>
/// 把容器里注册的 provider / sink 按宿主类型索引一次。本模块<b>从不点名</b>任何一个：
/// 业务模块自己注册进来，这里只按字符串找。
/// </remarks>
public class MergeSourceRegistry : IMergeSourceRegistry
{
    private readonly IReadOnlyDictionary<string, IMergeSourceProvider> _providers;
    private readonly IReadOnlyDictionary<string, IDocumentHostSink> _sinks;

    public MergeSourceRegistry(
        IEnumerable<IMergeSourceProvider> providers,
        IEnumerable<IDocumentHostSink> sinks)
    {
        Check.NotNull(providers);
        Check.NotNull(sinks);

        // 宿主类型名大小写不敏感：它来自持久化字符串与配置，让一次大小写差异
        // 变成"合并变量凭空消失"太不值得。
        // 同名重复注册取最后一个（与 DI 的 TryAdd/覆盖语义一致）。
        var providerMap = new Dictionary<string, IMergeSourceProvider>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in providers)
        {
            if (!string.IsNullOrWhiteSpace(p.EntityType))
                providerMap[p.EntityType.Trim()] = p;
        }

        var sinkMap = new Dictionary<string, IDocumentHostSink>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in sinks)
        {
            if (!string.IsNullOrWhiteSpace(s.EntityType))
                sinkMap[s.EntityType.Trim()] = s;
        }

        _providers = providerMap;
        _sinks = sinkMap;
    }

    /// <inheritdoc />
    public IMergeSourceProvider? FindProvider(string? entityType)
        => string.IsNullOrWhiteSpace(entityType)
            ? null
            : _providers.GetValueOrDefault(entityType.Trim());

    /// <inheritdoc />
    public IDocumentHostSink? FindSink(string? entityType)
        => string.IsNullOrWhiteSpace(entityType)
            ? null
            : _sinks.GetValueOrDefault(entityType.Trim());

    /// <inheritdoc />
    public IReadOnlyList<string> KnownHostTypes
        => _providers.Keys.Union(_sinks.Keys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToList();
}
