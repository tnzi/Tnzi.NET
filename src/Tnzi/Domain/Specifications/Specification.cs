
namespace Tnzi.Domain.Specifications;

/// <summary>
/// 规范基类
/// </summary>
/// <typeparam name="TEntity">实体类型</typeparam>
public abstract class Specification<TEntity> : ISpecification<TEntity>
{
    private Func<TEntity, bool>? _compiled;
    private readonly List<OrderByExpression<TEntity>> _orderByExpressions = [];
    private readonly List<Expression<Func<TEntity, object>>> _includeExpressions = [];
    private (int Skip, int Take)? _paging;

    /// <summary>
    /// 将规范转换为表达式
    /// </summary>
    public abstract Expression<Func<TEntity, bool>> ToExpression();

    /// <summary>
    /// 检查实体是否满足规范
    /// </summary>
    public virtual bool IsSatisfiedBy(TEntity entity)
    {
        _compiled ??= ToExpression().Compile();
        return _compiled(entity);
    }

    /// <summary>
    /// 获取排序表达式列表
    /// </summary>
    public IReadOnlyList<OrderByExpression<TEntity>> OrderByExpressions => _orderByExpressions;

    /// <summary>
    /// 获取 Include 表达式列表
    /// </summary>
    public IReadOnlyList<Expression<Func<TEntity, object>>> IncludeExpressions => _includeExpressions;

    /// <summary>
    /// 获取分页参数（null 表示不分页）
    /// </summary>
    public (int Skip, int Take)? Paging => _paging;

    #region Fluent Builder Methods

    /// <summary>
    /// 添加升序排序
    /// </summary>
    protected Specification<TEntity> OrderBy(Expression<Func<TEntity, object>> keySelector)
    {
        _orderByExpressions.Add(new OrderByExpression<TEntity>(keySelector));
        return this;
    }

    /// <summary>
    /// 添加降序排序
    /// </summary>
    protected Specification<TEntity> OrderByDescending(Expression<Func<TEntity, object>> keySelector)
    {
        _orderByExpressions.Add(new OrderByExpression<TEntity>(keySelector, Descending: true));
        return this;
    }

    /// <summary>
    /// 添加 Include 导航属性
    /// </summary>
    protected Specification<TEntity> Include(Expression<Func<TEntity, object>> includeExpression)
    {
        _includeExpressions.Add(includeExpression);
        return this;
    }

    /// <summary>
    /// 设置分页参数
    /// </summary>
    protected Specification<TEntity> WithPaging(int skip, int take)
    {
        _paging = (skip, take);
        return this;
    }

    #endregion

    #region Combination Operators

    /// <summary>
    /// AND操作：组合两个规范
    /// </summary>
    public static Specification<TEntity> operator &(Specification<TEntity> left, Specification<TEntity> right)
    {
        return new AndSpecification<TEntity>(left, right);
    }

    /// <summary>
    /// OR操作：组合两个规范
    /// </summary>
    public static Specification<TEntity> operator |(Specification<TEntity> left, Specification<TEntity> right)
    {
        return new OrSpecification<TEntity>(left, right);
    }

    /// <summary>
    /// NOT操作：取反规范
    /// </summary>
    public static Specification<TEntity> operator !(Specification<TEntity> specification)
    {
        return new NotSpecification<TEntity>(specification);
    }

    /// <summary>
    /// AND组合规范
    /// </summary>
    public Specification<TEntity> And(ISpecification<TEntity> other)
    {
        return new AndSpecification<TEntity>(this, other);
    }

    /// <summary>
    /// OR组合规范
    /// </summary>
    public Specification<TEntity> Or(ISpecification<TEntity> other)
    {
        return new OrSpecification<TEntity>(this, other);
    }

    /// <summary>
    /// NOT组合规范
    /// </summary>
    public Specification<TEntity> Not()
    {
        return new NotSpecification<TEntity>(this);
    }

    #endregion
}

/// <summary>
/// AND组合规范
/// </summary>
internal class AndSpecification<TEntity> : Specification<TEntity>
{
    private readonly ISpecification<TEntity> _left;
    private readonly ISpecification<TEntity> _right;

    public AndSpecification(ISpecification<TEntity> left, ISpecification<TEntity> right)
    {
        _left = left;
        _right = right;
    }

    public override Expression<Func<TEntity, bool>> ToExpression()
    {
        var leftExpression = _left.ToExpression();
        var rightExpression = _right.ToExpression();
        
        var parameter = Expression.Parameter(typeof(TEntity), "x");
        var leftBody = ReplaceParameter(leftExpression.Body, leftExpression.Parameters[0], parameter);
        var rightBody = ReplaceParameter(rightExpression.Body, rightExpression.Parameters[0], parameter);
        
        var andExpression = Expression.AndAlso(leftBody, rightBody);
        return Expression.Lambda<Func<TEntity, bool>>(andExpression, parameter);
    }

    private static Expression ReplaceParameter(Expression expression, ParameterExpression oldParameter, ParameterExpression newParameter)
    {
        return new ParameterReplacer(oldParameter, newParameter).Visit(expression);
    }
}

/// <summary>
/// OR组合规范
/// </summary>
internal class OrSpecification<TEntity> : Specification<TEntity>
{
    private readonly ISpecification<TEntity> _left;
    private readonly ISpecification<TEntity> _right;

    public OrSpecification(ISpecification<TEntity> left, ISpecification<TEntity> right)
    {
        _left = left;
        _right = right;
    }

    public override Expression<Func<TEntity, bool>> ToExpression()
    {
        var leftExpression = _left.ToExpression();
        var rightExpression = _right.ToExpression();
        
        var parameter = Expression.Parameter(typeof(TEntity), "x");
        var leftBody = ReplaceParameter(leftExpression.Body, leftExpression.Parameters[0], parameter);
        var rightBody = ReplaceParameter(rightExpression.Body, rightExpression.Parameters[0], parameter);
        
        var orExpression = Expression.OrElse(leftBody, rightBody);
        return Expression.Lambda<Func<TEntity, bool>>(orExpression, parameter);
    }

    private static Expression ReplaceParameter(Expression expression, ParameterExpression oldParameter, ParameterExpression newParameter)
    {
        return new ParameterReplacer(oldParameter, newParameter).Visit(expression);
    }
}

/// <summary>
/// NOT组合规范
/// </summary>
internal class NotSpecification<TEntity> : Specification<TEntity>
{
    private readonly ISpecification<TEntity> _specification;

    public NotSpecification(ISpecification<TEntity> specification)
    {
        _specification = specification;
    }

    public override Expression<Func<TEntity, bool>> ToExpression()
    {
        var expression = _specification.ToExpression();
        var parameter = expression.Parameters[0];
        var body = Expression.Not(expression.Body);
        return Expression.Lambda<Func<TEntity, bool>>(body, parameter);
    }
}

/// <summary>
/// 参数替换访问器
/// </summary>
internal class ParameterReplacer : ExpressionVisitor
{
    private readonly ParameterExpression _oldParameter;
    private readonly ParameterExpression _newParameter;

    public ParameterReplacer(ParameterExpression oldParameter, ParameterExpression newParameter)
    {
        _oldParameter = oldParameter;
        _newParameter = newParameter;
    }

    protected override Expression VisitParameter(ParameterExpression node)
    {
        return node == _oldParameter ? _newParameter : base.VisitParameter(node);
    }
}