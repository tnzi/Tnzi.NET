
namespace Tnzi.Data.Filtering;

/// <summary>
/// Marks a DTO property as a filter criterion for FilterExpressionBuilder.BuildFromDto.
/// When applied to a DTO property, the builder will automatically create a filter rule
/// from the property value, mapping it to the specified entity field with the given operator.
/// </summary>
/// <remarks>
/// Usage:
/// <code>
/// public class ProductQueryDto : PagedQueryDto
/// {
///     [FilterProperty(Operator = FilterOperator.Contains)]
///     public string? Name { get; set; }
///
///     [FilterProperty("CategoryId", Operator = FilterOperator.Equal)]
///     public Guid? Category { get; set; }
///
///     [FilterProperty(Operator = FilterOperator.GreaterThanOrEqual)]
///     public decimal? MinPrice { get; set; }
/// }
///
/// // Build filter expression from DTO
/// var expression = FilterExpressionBuilder.BuildFromDto&lt;ProductEntity, ProductQueryDto&gt;(dto);
/// </code>
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class FilterPropertyAttribute : Attribute
{
    /// <summary>
    /// The target entity property name. If null, uses the DTO property name.
    /// Supports nested properties (e.g. "Author.Name").
    /// </summary>
    public string? EntityProperty { get; }

    /// <summary>
    /// The filter operator to apply (default: Equal)
    /// </summary>
    public FilterOperator Operator { get; set; } = FilterOperator.Equal;

    /// <summary>
    /// The logical operator for combining this filter with others (default: And)
    /// </summary>
    public LogicalOperator Logic { get; set; } = LogicalOperator.And;

    /// <summary>
    /// Creates a FilterProperty attribute that maps to the same-named entity property
    /// </summary>
    public FilterPropertyAttribute()
    {
    }

    /// <summary>
    /// Creates a FilterProperty attribute that maps to a specific entity property
    /// </summary>
    /// <param name="entityProperty">Target entity property name (supports nested, e.g. "Author.Name")</param>
    public FilterPropertyAttribute(string entityProperty)
    {
        EntityProperty = entityProperty;
    }
}
