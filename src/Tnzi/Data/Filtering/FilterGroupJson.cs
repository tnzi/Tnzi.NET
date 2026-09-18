namespace Tnzi.Data.Filtering;

/// <summary>
/// <see cref="FilterGroup"/> 的 JSON 方言：<b>一处定义</b>，所有把过滤器当字符串落库或从请求里读进来的地方共用。
/// </summary>
/// <remarks>
/// <para>
/// 方言 = 属性名大小写不敏感（<c>logic</c> / <c>Logic</c> 都认）+ 枚举按名字（<c>"operator":"Equal"</c>）。
/// 这与列表 API 的 <see cref="FilterQueryableExtensions.ApplyFilterFromJson{T}"/> 逐字相同，
/// 所以一份在列表接口上合法的过滤器 JSON，在行级数据授权那边也是同一个意思。
/// </para>
/// <para>
/// ★ 为什么不用 <c>JsonExtensions.FromJsonString&lt;T&gt;</c>：那个扩展方法吞掉一切异常返回 <c>default</c>，
/// 而且是大小写敏感、没有枚举转换器 —— 一份 camelCase 的过滤器会反序列化成「零条规则」，一份字符串枚举会
/// 变成 <c>null</c>；两种结果对安全过滤器来说都读作「没有限制」，方向是放开且零症状。
/// 过滤器不是普通载荷：解析失败必须是一个<b>可区分的结果</b>，调用方才能拒绝而不是放行。
/// </para>
/// </remarks>
public static class FilterGroupJson
{
    internal static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// 解析一段过滤器 JSON。空白输入、非法 JSON、以及字面量 <c>null</c> 都算失败并给出原因；
    /// 成功时 <paramref name="group"/> 非空，但<b>可能没有任何规则</b>（<see cref="FilterGroup.HasFilters"/> 为 false），
    /// 那是「写了却没规则」，由调用方按自己的语义处置。
    /// </summary>
    /// <param name="json">过滤器 JSON。</param>
    /// <param name="group">解析结果；失败时为 <c>null</c>。</param>
    /// <param name="error">失败原因（面向管理员的英文文案）；成功时为 <c>null</c>。</param>
    /// <returns>是否解析成功。</returns>
    public static bool TryParse(string? json, out FilterGroup? group, out string? error)
    {
        group = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            error = "Filter JSON is empty.";
            return false;
        }

        try
        {
            group = JsonSerializer.Deserialize<FilterGroup>(json, SerializerOptions);
        }
        catch (JsonException ex)
        {
            error = $"Filter JSON is malformed: {ex.Message}";
            return false;
        }

        if (group == null)
        {
            error = "Filter JSON is the literal null.";
            return false;
        }

        error = null;
        return true;
    }
}
