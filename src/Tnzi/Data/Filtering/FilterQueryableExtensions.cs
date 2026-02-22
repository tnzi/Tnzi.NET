
namespace Tnzi.Data.Filtering;

/// <summary>
/// IQueryable 过滤扩展方法
/// </summary>
public static class FilterQueryableExtensions
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// 应用过滤条件组
    /// </summary>
    public static IQueryable<T> ApplyFilter<T>(this IQueryable<T> query, FilterGroup? filter)
    {
        if (filter == null || !filter.HasFilters)
        {
            return query;
        }

        var expression = FilterExpressionBuilder.Build<T>(filter);
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
    /// 从 JSON 字符串应用过滤条件
    /// </summary>
    public static IQueryable<T> ApplyFilterFromJson<T>(this IQueryable<T> query, string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return query;
        }

        var filter = JsonSerializer.Deserialize<FilterGroup>(json, JsonOptions);
        return query.ApplyFilter(filter);
    }

    /// <summary>
    /// 尝试从 JSON 字符串应用过滤条件
    /// 如果 JSON 无效或表达式构建失败，返回原始查询
    /// </summary>
    public static IQueryable<T> TryApplyFilterFromJson<T>(
        this IQueryable<T> query, 
        string? json, 
        out string? error)
    {
        error = null;
        
        if (string.IsNullOrWhiteSpace(json))
        {
            return query;
        }

        try
        {
            var filter = JsonSerializer.Deserialize<FilterGroup>(json, JsonOptions);
            if (filter == null || !filter.HasFilters)
            {
                return query;
            }

            if (!FilterExpressionBuilder.TryBuild<T>(filter, out var expression, out error))
            {
                return query;
            }

            return query.Where(expression!);
        }
        catch (JsonException ex)
        {
            error = $"Invalid JSON format: {ex.Message}";
            return query;
        }
    }
}