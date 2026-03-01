
namespace Tnzi.Domain.Specifications;

/// <summary>
/// 规范接口
/// </summary>
/// <typeparam name="TEntity">实体类型</typeparam>
public interface ISpecification<TEntity>
{
    /// <summary>
    /// 将规范转换为表达式
    /// </summary>
    Expression<Func<TEntity, bool>> ToExpression();

    /// <summary>
    /// 检查实体是否满足规范
    /// </summary>
    bool IsSatisfiedBy(TEntity entity);

    /// <summary>
    /// 获取排序表达式列表
    /// </summary>
    IReadOnlyList<OrderByExpression<TEntity>> OrderByExpressions => [];

    /// <summary>
    /// 获取 Include 表达式列表
    /// </summary>
    IReadOnlyList<Expression<Func<TEntity, object>>> IncludeExpressions => [];

    /// <summary>
    /// 获取分页参数（null 表示不分页）
    /// </summary>
    (int Skip, int Take)? Paging => null;
}

/// <summary>
/// 排序表达式
/// </summary>
public record OrderByExpression<TEntity>(Expression<Func<TEntity, object>> KeySelector, bool Descending = false);
