namespace Tnzi.Data;

/// <summary>
/// 可插拔的行级数据范围 provider。让应用把「因为当前用户被指派到关联实体、所以此行可见」
/// 这类自定义可见性谓词插入框架查询，替代在服务里手搓 scope 过滤栈。
/// </summary>
/// <remarks>
/// <para><b>opt-in</b>：容器里未注册任何 <see cref="IDataScopeProvider{TEntity}"/> 时零影响
/// （<see cref="DataScopeExtensions"/> 的合成结果为 <c>null</c>，查询原样返回）。</para>
/// <para><b>在哪里生效</b>：<see cref="Tnzi.Application.CrudAppService{TEntity,TKey,TDto,TCreateDto,TUpdateDto}"/>
/// 自动把注册的 <see cref="GetFilter"/> 施加到列表、取详情 / 更新 / 删除与批删上；自己写查询的服务经
/// <see cref="DataScopeExtensions"/> 的 <c>ApplyDataScope</c> / <c>BuildDataScopeFilter</c> 显式施加，
/// 没有调用它们的查询不受范围约束。<see cref="CanAccessAsync"/> 只在代码显式调用
/// <c>DataScopeExtensions.CanAccessAsync</c> / <c>CanAccessByIdAsync</c> 时参与判定。</para>
/// <para>注册多个同实体 provider 时，它们的 <see cref="GetFilter"/> 谓词以 <b>AND</b> 组合
/// （逐层收紧）——见 <see cref="DataScopeExtensions.BuildDataScopeFilter{TEntity}(System.Collections.Generic.IEnumerable{IDataScopeProvider{TEntity}})"/>。</para>
/// <para>这是「行级可见性」的可组合抽象，区别于 <see cref="IDataFilter"/> 家族的全局过滤器
/// （软删除 / 多租户，由 DbContext 无条件施加）。</para>
/// </remarks>
/// <typeparam name="TEntity">实体类型</typeparam>
[StableApi(Since = "0.1.0")]
public interface IDataScopeProvider<TEntity>
    where TEntity : class
{
    /// <summary>
    /// 返回本范围对 <typeparamref name="TEntity"/> 的过滤谓词；
    /// <c>null</c> 表示本 provider 不施加任何限制（放行全部）。
    /// </summary>
    Expression<Func<TEntity, bool>>? GetFilter();

    /// <summary>
    /// 判定当前主体能否访问指定实体。默认实现基于 <see cref="GetFilter"/> 谓词在内存中求值
    /// （无谓词=放行）。需要按 id 校验时用 <see cref="DataScopeExtensions"/> 的仓储扩展先加载实体。
    /// </summary>
    /// <remarks>
    /// 内存求值与数据库查询的语义不同：谓词引用的导航必须已经加载在 <paramref name="entity"/> 上
    /// （按 id 加载的实体不带导航，集合为空、引用为 null），字符串按序号比较而不是数据库的排序规则。
    /// 引用导航的谓词要与列表同一语义，把它交给查询（<c>ApplyDataScope</c>）。
    /// </remarks>
    /// <param name="entity">待校验的实体（已加载）</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<bool> CanAccessAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        Check.NotNull(entity);
        var filter = GetFilter();
        return Task.FromResult(filter == null || filter.Compile()(entity));
    }
}
