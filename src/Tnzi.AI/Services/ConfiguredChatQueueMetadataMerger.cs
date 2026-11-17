namespace Tnzi.AI.Services;

/// <summary>
/// <see cref="IChatQueueMetadataMerger"/> 的默认实现：<c>AI:Chat:Queue:StrictestValues</c> 列出的键取最严的值，其余的键按先后合并、后写的为准。
/// </summary>
/// <remarks>
/// <para>
/// <c>StrictestValues</c> 是「键 → 值的清单，越靠前越严」，例如 <c>"permissionMode": ["plan", "ask", "full"]</c>：
/// 几条消息里出现过的值中取最靠前的那个。只比较字符串值。
/// </para>
/// <para>
/// 取不到排名的值（不在清单里、不是字符串）按<b>最严</b>算：它会胜出并原样交下去，由应用自己的处理器判断；
/// 否则一条带着未知值的消息会被一条「全权」盖掉，那正是这个合并要防的事。几条都没有这个键时结果里也没有它
/// （没有值就是应用的默认，合并不替应用补值）。
/// </para>
/// </remarks>
public class ConfiguredChatQueueMetadataMerger : IChatQueueMetadataMerger
{
    private readonly IOptionsMonitor<AiChatOptions> _options;

    /// <summary>初始化。</summary>
    public ConfiguredChatQueueMetadataMerger(IOptionsMonitor<AiChatOptions> options)
    {
        _options = Check.NotNull(options);
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, JsonElement>?> MergeAsync(ChatQueueMergeContext context, CancellationToken cancellationToken = default)
    {
        Check.NotNull(context);
        return Task.FromResult(Merge(context.Metadata, _options.CurrentValue.Queue.StrictestValues));
    }

    /// <summary>按先后合并，<paramref name="strictest"/> 列出的键取最严的值。没有任何键时返回 null。</summary>
    public static IReadOnlyDictionary<string, JsonElement>? Merge(
        IReadOnlyList<IReadOnlyDictionary<string, JsonElement>> metadata, IReadOnlyDictionary<string, List<string>>? strictest)
    {
        Check.NotNull(metadata);

        var merged = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var part in metadata)
        {
            foreach (var (key, value) in part)
            {
                merged[key] = merged.TryGetValue(key, out var earlier) && strictest?.GetValueOrDefault(key) is { } ranking
                    ? Stricter(earlier, value, ranking)
                    : value;
            }
        }

        return merged.Count > 0 ? merged : null;
    }

    /// <summary>两个值里更严的那个；一样严时取后写的。</summary>
    private static JsonElement Stricter(JsonElement earlier, JsonElement later, List<string> ranking)
        => Rank(later, ranking) <= Rank(earlier, ranking) ? later : earlier;

    /// <summary>值在清单里的位置（越小越严）；取不到排名的值按最严算（-1）。</summary>
    private static int Rank(JsonElement value, List<string> ranking)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            return -1;
        }

        var index = ranking.IndexOf(value.GetString()!);
        return index < 0 ? -1 : index;
    }
}
