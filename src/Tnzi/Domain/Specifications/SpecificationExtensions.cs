
namespace Tnzi.Domain.Specifications;

/// <summary>
/// ISpecification 组合扩展方法，支持 And/Or/Not 流式组合
/// </summary>
public static class SpecificationExtensions
{
    /// <summary>
    /// AND 组合两个规范
    /// </summary>
    public static ISpecification<T> And<T>(this ISpecification<T> left, ISpecification<T> right)
    {
        Check.NotNull(left);
        Check.NotNull(right);
        return new AndSpecification<T>(left, right);
    }

    /// <summary>
    /// OR 组合两个规范
    /// </summary>
    public static ISpecification<T> Or<T>(this ISpecification<T> left, ISpecification<T> right)
    {
        Check.NotNull(left);
        Check.NotNull(right);
        return new OrSpecification<T>(left, right);
    }

    /// <summary>
    /// NOT 取反规范
    /// </summary>
    public static ISpecification<T> Not<T>(this ISpecification<T> spec)
    {
        Check.NotNull(spec);
        return new NotSpecification<T>(spec);
    }
}
