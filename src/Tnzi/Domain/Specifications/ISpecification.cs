
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
}