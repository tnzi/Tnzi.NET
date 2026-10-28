
namespace Tnzi.Data.Filtering;

/// <summary>
/// IQueryable 过滤扩展方法
/// </summary>
/// <remarks>
/// ★ 不带 <see cref="FilterFieldPolicy"/> 的重载按 <see cref="FilterFieldPolicy.Unrestricted"/> 放行任何公共属性路径，
/// 只给服务端自建的过滤器用。过滤器来自请求（查询串、请求体里的 JSON）时用带策略的重载
/// （通常是 <c>FilterFieldPolicy.ProjectedBy(typeof(返回的 DTO))</c>）：否则调用方能按从不投影的列
/// （口令哈希、安全戳）用 <c>StartsWith</c> 逐字符二分，结果行数就是答案。
/// </remarks>
public static class FilterQueryableExtensions
{
    /// <summary>
    /// 应用过滤条件组（不限制字段：只用于服务端自建的过滤器）
    /// </summary>
    public static IQueryable<T> ApplyFilter<T>(this IQueryable<T> query, FilterGroup? filter)
    {
        return query.ApplyFilter(filter, FilterFieldPolicy.Unrestricted);
    }

    /// <summary>
    /// 应用过滤条件组，字段与规则数先经 <paramref name="policy"/> 准入；不允许时抛 400 的
    /// <see cref="FilterFieldException"/> / <c>ValidationException</c>
    /// </summary>
    public static IQueryable<T> ApplyFilter<T>(this IQueryable<T> query, FilterGroup? filter, FilterFieldPolicy policy)
    {
        Check.NotNull(query);
        Check.NotNull(policy);
        if (filter == null || !filter.HasFilters)
        {
            return query;
        }

        var expression = FilterExpressionBuilder.Build<T>(filter, policy);
        return query.Where(expression);
    }

    /// <summary>
    /// 应用单个过滤规则
    /// </summary>
    public static IQueryable<T> ApplyFilter<T>(this IQueryable<T> query, FilterRule rule)
    {
        var expression = FilterExpressionBuilder.Build<T>(rule);
        return query.Where(expression);
    }

    /// <summary>
    /// 从 JSON 字符串应用过滤条件（不限制字段：只用于服务端自建的过滤器）
    /// </summary>
    public static IQueryable<T> ApplyFilterFromJson<T>(this IQueryable<T> query, string? json)
    {
        return query.ApplyFilterFromJson(json, FilterFieldPolicy.Unrestricted);
    }

    /// <summary>
    /// 从 JSON 字符串应用过滤条件，字段与规则数先经 <paramref name="policy"/> 准入；不允许时抛 400 的
    /// <see cref="FilterFieldException"/> / <c>ValidationException</c>
    /// </summary>
    public static IQueryable<T> ApplyFilterFromJson<T>(this IQueryable<T> query, string? json, FilterFieldPolicy policy)
    {
        Check.NotNull(query);
        Check.NotNull(policy);
        if (string.IsNullOrWhiteSpace(json))
        {
            return query;
        }

        var filter = JsonSerializer.Deserialize<FilterGroup>(json, FilterGroupJson.SerializerOptions);
        return query.ApplyFilter(filter, policy);
    }

    /// <summary>
    /// 尝试从 JSON 字符串应用过滤条件（不限制字段：只用于服务端自建的过滤器）。
    /// 如果 JSON 无效或表达式构建失败，返回原始查询并给出 <paramref name="error"/>
    /// </summary>
    /// <remarks>
    /// 失败时返回的是<b>未经过滤</b>的原始查询：调用方必须先看 <paramref name="error"/>，
    /// 不能把返回值当成已经按条件收窄的结果。
    /// </remarks>
    public static IQueryable<T> TryApplyFilterFromJson<T>(
        this IQueryable<T> query,
        string? json,
        out string? error)
    {
        return query.TryApplyFilterFromJson(json, FilterFieldPolicy.Unrestricted, out error);
    }

    /// <summary>
    /// 尝试从 JSON 字符串应用过滤条件，字段与规则数先经 <paramref name="policy"/> 准入。
    /// JSON 无效、字段不被允许或表达式构建失败时返回原始查询并给出 <paramref name="error"/>
    /// </summary>
    /// <remarks>
    /// 失败时返回的是<b>未经过滤</b>的原始查询：调用方必须先看 <paramref name="error"/>，
    /// 不能把返回值当成已经按条件收窄的结果。
    /// </remarks>
    public static IQueryable<T> TryApplyFilterFromJson<T>(
        this IQueryable<T> query,
        string? json,
        FilterFieldPolicy policy,
        out string? error)
    {
        Check.NotNull(query);
        Check.NotNull(policy);
        error = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            return query;
        }

        FilterGroup? filter;
        try
        {
            filter = JsonSerializer.Deserialize<FilterGroup>(json, FilterGroupJson.SerializerOptions);
        }
        catch (JsonException ex)
        {
            error = $"Invalid JSON format: {ex.Message}";
            return query;
        }

        if (filter == null || !filter.HasFilters)
        {
            return query;
        }

        try
        {
            return query.Where(FilterExpressionBuilder.Build<T>(filter, policy));
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return query;
        }
    }
}