using Tnzi.Domain.Repositories;
using Tnzi.Mapping;

namespace Tnzi.Application;

/// <summary>
/// 泛型 CRUD 应用服务基类。为「实体 + 三类 DTO」的标准增删改查提供开箱即用的
/// <see cref="Result{T}"/> 语义方法（查询/取详情/新建/更新/删除/批删），内部经
/// <see cref="IRepository{TEntity,TKey}"/> 读写、经 <see cref="IObjectMapper"/> 做 实体↔DTO 映射。
/// </summary>
/// <remarks>
/// <para><b>opt-in（不影响现有服务）</b>：继承本类纯属可选，现有手写服务不受影响。</para>
/// <para><b>可覆写钩子</b>：所有端点方法与生命周期钩子均为 <c>virtual</c>——
/// <see cref="ApplyScopeAsync"/>（行级数据范围谓词）、
/// <see cref="BeforeCreateAsync"/> / <see cref="AfterCreateAsync"/> /
/// <see cref="BeforeUpdateAsync"/> / <see cref="AfterUpdateAsync"/> /
/// <see cref="BeforeDeleteAsync"/> / <see cref="AfterDeleteAsync"/>，消费者按需 override。</para>
/// <para><b>为何 <see cref="ApplyScopeAsync"/> 返回谓词而非改写 <c>IQueryable</c></b>：
/// 核心程序集不引用 EF Core，无法对任意投影后的 <c>IQueryable</c> 做异步物化；
/// 返回可组合的 <see cref="Expression{TDelegate}"/> 谓词，交由仓储层 AND 到查询里
/// （与 <see cref="Tnzi.Data.Filtering.FilterGroup"/> 动态过滤共存），既保持核心纯净又与
/// <see cref="Tnzi.Data.IDataScopeProvider{TEntity}"/> 可插拔范围机制同构。</para>
/// </remarks>
/// <typeparam name="TEntity">实体类型</typeparam>
/// <typeparam name="TKey">主键类型</typeparam>
/// <typeparam name="TDto">读取/返回 DTO</typeparam>
/// <typeparam name="TCreateDto">创建输入 DTO</typeparam>
/// <typeparam name="TUpdateDto">更新输入 DTO</typeparam>
[ExperimentalApi(Reason = "Generic CRUD application-service base is an opt-in convenience surface still stabilising")]
public abstract class CrudAppService<TEntity, TKey, TDto, TCreateDto, TUpdateDto>
    : ApplicationService, ICrudAppService<TKey, TDto, TCreateDto, TUpdateDto>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private const string EntityNotFoundMessage = "The requested resource was not found.";

    private readonly Lazy<IObjectMapper> _mapper;

    /// <summary>
    /// 实体仓储。
    /// </summary>
    protected IRepository<TEntity, TKey> Repository { get; }

    /// <summary>
    /// 对象映射器（延迟解析，线程安全）。
    /// </summary>
    protected IObjectMapper Mapper => _mapper.Value;

    /// <summary>
    /// 初始化 CRUD 应用服务基类。
    /// </summary>
    /// <param name="serviceProvider">服务提供者</param>
    /// <param name="repository">实体仓储</param>
    protected CrudAppService(IServiceProvider serviceProvider, IRepository<TEntity, TKey> repository)
        : base(serviceProvider)
    {
        Repository = Check.NotNull(repository);
        _mapper = new Lazy<IObjectMapper>(
            () => GetRequiredService<IObjectMapper>(),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>
    /// 分页查询。应用 <see cref="ApplyScopeAsync"/> 行级范围谓词与 <see cref="PagedQuery.Filter"/> 动态过滤。
    /// </summary>
    public virtual async Task<Result<IPagedList<TDto>>> QueryAsync(PagedQuery query, CancellationToken cancellationToken = default)
    {
        Check.NotNull(query);

        var scope = await ApplyScopeAsync(cancellationToken);
        var page = scope == null
            ? await Repository.GetPagedListAsync(query, cancellationToken)
            : await Repository.GetPagedListAsync(scope, query, cancellationToken);

        var dtos = Mapper.MapToList<TDto>(page.Items.Cast<object>());
        IPagedList<TDto> result = new PagedList<TDto>(dtos, page.PageIndex, page.PageSize, page.TotalCount);
        return Ok(result);
    }

    /// <summary>
    /// 按主键取详情。范围外/不存在一律返回 404（避免存在性泄漏）。
    /// </summary>
    public virtual async Task<Result<TDto>> GetByIdAsync(TKey id, CancellationToken cancellationToken = default)
    {
        var entity = await Repository.GetAsync(id, cancellationToken);
        if (entity == null || !await IsInScopeAsync(entity, cancellationToken))
            return Fail<TDto>(EntityNotFoundMessage, 404, ErrorCodes.RESOURCE_NOT_FOUND);

        return Ok(Mapper.Map<TDto>(entity));
    }

    /// <summary>
    /// 新建。整个流程在工作单元内执行（自动提交/回滚）。
    /// </summary>
    public virtual async Task<Result<TDto>> CreateAsync(TCreateDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        return await ExecuteInUnitOfWorkAsync(async ct =>
        {
            var entity = Mapper.Map<TEntity>(input!);
            await BeforeCreateAsync(input, entity, ct);
            await Repository.InsertAsync(entity, ct);
            await AfterCreateAsync(entity, ct);
            // Flush so the generated Id + creation-audit fields (CreationTime/CreatorId,
            // populated inside SaveChanges) are set before we project the response DTO.
            // Under an enabled UoW transaction InsertAsync only tracks the entity (save
            // is deferred to commit), so without this the DTO would carry an empty Id and
            // a 0001-01-01 timestamp. Repository.SaveChangesAsync routes through the UoW —
            // it starts the deferred physical transaction rather than autocommitting.
            await Repository.SaveChangesAsync(ct);
            return Ok(Mapper.Map<TDto>(entity));
        }, cancellationToken);
    }

    /// <summary>
    /// 更新。范围外/不存在一律返回 404。整个流程在工作单元内执行。
    /// </summary>
    public virtual async Task<Result<TDto>> UpdateAsync(TKey id, TUpdateDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        return await ExecuteInUnitOfWorkAsync(async ct =>
        {
            var entity = await Repository.GetAsync(id, ct);
            if (entity == null || !await IsInScopeAsync(entity, ct))
                return Fail<TDto>(EntityNotFoundMessage, 404, ErrorCodes.RESOURCE_NOT_FOUND);

            await BeforeUpdateAsync(input, entity, ct);
            Mapper.Map(input, entity);
            await Repository.UpdateAsync(entity, ct);
            await AfterUpdateAsync(entity, ct);
            // Flush so LastModificationTime/LastModifierId (populated inside SaveChanges)
            // are set before projecting the response DTO (see CreateAsync for the
            // deferred-save rationale). Transaction-safe via Repository.SaveChangesAsync.
            await Repository.SaveChangesAsync(ct);
            return Ok(Mapper.Map<TDto>(entity));
        }, cancellationToken);
    }

    /// <summary>
    /// 按主键删除。范围外/不存在一律返回 404。
    /// </summary>
    public virtual async Task<Result> DeleteAsync(TKey id, CancellationToken cancellationToken = default)
    {
        return await ExecuteInUnitOfWorkAsync(async ct =>
        {
            var entity = await Repository.GetAsync(id, ct);
            if (entity == null || !await IsInScopeAsync(entity, ct))
                return Fail(EntityNotFoundMessage, 404, ErrorCodes.RESOURCE_NOT_FOUND);

            await BeforeDeleteAsync(entity, ct);
            await Repository.DeleteAsync(entity, ct);
            await AfterDeleteAsync(entity, ct);
            return Ok();
        }, cancellationToken);
    }

    /// <summary>
    /// 批量删除。范围外的 id 静默跳过（不报错，避免暴露跨范围数据）。
    /// </summary>
    public virtual async Task<Result> BatchDeleteAsync(IEnumerable<TKey> ids, CancellationToken cancellationToken = default)
    {
        Check.NotNull(ids);
        var idList = ids.Distinct().ToList();
        if (idList.Count == 0)
            return Ok();

        return await ExecuteInUnitOfWorkAsync(async ct =>
        {
            var entities = await Repository.GetListAsync(idList, ct);

            var scope = await ApplyScopeAsync(ct);
            if (scope != null)
            {
                var predicate = scope.Compile();
                entities = entities.Where(predicate).ToList();
            }

            if (entities.Count == 0)
                return Ok();

            foreach (var entity in entities)
                await BeforeDeleteAsync(entity, ct);

            await Repository.DeleteManyAsync(entities, ct);

            foreach (var entity in entities)
                await AfterDeleteAsync(entity, ct);

            return Ok();
        }, cancellationToken);
    }

    /// <summary>
    /// 行级数据范围谓词。默认返回 <c>null</c>（不限制）。override 以注入自定义可见性规则
    /// （例如「因为用户被指派到关联实体所以此行可见」），谓词会被 AND 到查询与单行访问校验中。
    /// </summary>
    protected virtual Task<Expression<Func<TEntity, bool>>?> ApplyScopeAsync(CancellationToken cancellationToken)
        => Task.FromResult<Expression<Func<TEntity, bool>>?>(null);

    /// <summary>
    /// 判定单个实体是否落在当前范围内（用于取详情/更新/删除的行级校验）。
    /// 默认编译 <see cref="ApplyScopeAsync"/> 谓词在内存中求值；无谓词时恒为 true。
    /// </summary>
    protected virtual async Task<bool> IsInScopeAsync(TEntity entity, CancellationToken cancellationToken)
    {
        var scope = await ApplyScopeAsync(cancellationToken);
        return scope == null || scope.Compile()(entity);
    }

    /// <summary>创建前钩子：可校验/补全实体（可抛 <see cref="Tnzi.Exceptions.BusinessException"/> 拒绝）。</summary>
    protected virtual Task BeforeCreateAsync(TCreateDto input, TEntity entity, CancellationToken cancellationToken)
        => Task.CompletedTask;

    /// <summary>创建后钩子：可发事件/写辅助数据。</summary>
    protected virtual Task AfterCreateAsync(TEntity entity, CancellationToken cancellationToken)
        => Task.CompletedTask;

    /// <summary>更新前钩子：在把 <typeparamref name="TUpdateDto"/> 映射进实体之前执行。</summary>
    protected virtual Task BeforeUpdateAsync(TUpdateDto input, TEntity entity, CancellationToken cancellationToken)
        => Task.CompletedTask;

    /// <summary>更新后钩子。</summary>
    protected virtual Task AfterUpdateAsync(TEntity entity, CancellationToken cancellationToken)
        => Task.CompletedTask;

    /// <summary>删除前钩子。</summary>
    protected virtual Task BeforeDeleteAsync(TEntity entity, CancellationToken cancellationToken)
        => Task.CompletedTask;

    /// <summary>删除后钩子。</summary>
    protected virtual Task AfterDeleteAsync(TEntity entity, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
