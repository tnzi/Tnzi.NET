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
/// <para><b>行级数据范围</b>：容器里为 <typeparamref name="TEntity"/> 注册的每个
/// <see cref="Tnzi.Data.IDataScopeProvider{TEntity}"/> 的 <c>GetFilter()</c> 谓词，与 <see cref="ApplyScopeAsync"/>
/// 的结果一起以 AND 施加到列表查询、取详情 / 更新 / 删除的单行校验与批删上。注册了 provider 就生效，
/// 不需要覆写任何东西；覆写 <see cref="ApplyScopeAsync"/> 只会在此之上再收紧，不会把注册的 provider 换掉。
/// 未注册任何 provider 且未覆写时不施加限制。provider 覆写的 <c>CanAccessAsync</c> 不参与这里的判定：
/// 列表只能用谓词，单行校验与它同源才不会出现「列表看得见、详情 404」或反过来。
/// 单行校验与批删同样<b>交给数据库判定</b>（按主键 AND 范围谓词查询），与列表同一套 SQL 语义：
/// 引用导航的谓词（<c>p =&gt; p.Assignments.Any(a =&gt; a.UserId == me)</c>）不需要先加载导航，
/// 字符串比较也按数据库的排序规则。</para>
/// <para><b>范围只约束读取与对既有行的访问，不约束写入的结果</b>：新建的实体不过范围谓词，更新只校验映射前的那一行。
/// <typeparamref name="TCreateDto"/> / <typeparamref name="TUpdateDto"/> 若带范围字段（部门、归属人），调用方可以把记录建进
/// 或移进别人的范围。可见性谓词不能直接当写入规则用（「指派给我才可见」的新记录在指派之前就不在范围内；跨部门移交是正当的更新），
/// 需要写入约束的服务自己校验：新建在 <see cref="BeforeCreateAsync"/>（收到的是映射后的实体），更新在 <see cref="AfterUpdateAsync"/>
/// （映射之后、保存之前，抛异常整个工作单元回滚），例如对实体调 <see cref="IsInScopeAsync"/> 不通过就抛
/// <see cref="Tnzi.Exceptions.ForbiddenException"/>；或者干脆不在输入 DTO 上暴露范围字段。</para>
/// <para><b>可覆写钩子</b>：所有端点方法与生命周期钩子均为 <c>virtual</c>——
/// <see cref="ApplyScopeAsync"/>（服务自己的行级数据范围谓词）、
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
    where TCreateDto : notnull
    where TUpdateDto : notnull
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

    // 每个封闭泛型算一次：TDto 的公共标量属性名就是请求方看得见的列
    private static readonly FilterFieldPolicy ProjectedFilterPolicy = FilterFieldPolicy.ProjectedBy(typeof(TDto));

    /// <summary>
    /// 请求来源的 <see cref="PagedQuery.Filter"/> 与 <see cref="PagedQuery.OrderBy"/> 的字段准入策略。
    /// 默认 <see cref="FilterFieldPolicy.ProjectedBy"/>(<typeparamref name="TDto"/>)：只允许 DTO 上同名的根标量列 ——
    /// 口令哈希、安全戳、薪资这类实体有而 DTO 从不投影的根列对请求方不存在，导航路径一律拒绝。
    /// 需要更宽的服务显式放宽（<see cref="FilterFieldPolicy.AllowOnly"/> 点名，或
    /// <c>FilterFieldPolicy.Request with { AllowNavigation = true, MaxDepth = 2 }</c>，后者放行根实体全部标量列）。
    /// </summary>
    protected virtual FilterFieldPolicy FilterPolicy => ProjectedFilterPolicy;

    /// <summary>
    /// 分页查询。应用 <see cref="ApplyScopeAsync"/> 行级范围谓词与 <see cref="PagedQuery.Filter"/> 动态过滤；
    /// 过滤与排序字段先过 <see cref="FilterPolicy"/>，不在允许范围内的答 400（消息不含实体类型名）。
    /// </summary>
    public virtual async Task<Result<IPagedList<TDto>>> QueryAsync(PagedQuery query, CancellationToken cancellationToken = default)
    {
        Check.NotNull(query);

        var policy = FilterPolicy;
        if (!FilterExpressionBuilder.TryValidateFields<TEntity>(query.Filter, policy, out var filterError))
            return Fail<IPagedList<TDto>>(filterError!, 400, ErrorCodes.VALIDATION_ERROR);
        if (!FilterExpressionBuilder.TryValidateOrderBy<TEntity>(query.OrderBy, policy, out var orderByError))
            return Fail<IPagedList<TDto>>(orderByError!, 400, ErrorCodes.VALIDATION_ERROR);

        var scope = await ResolveScopeAsync(cancellationToken);
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
        var entity = await GetInScopeAsync(id, cancellationToken);
        if (entity == null)
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
            // a 0001-01-01 timestamp. Repository.SaveChangesAsync routes through the UoW -
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
            var entity = await GetInScopeAsync(id, ct);
            if (entity == null)
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
            var entity = await GetInScopeAsync(id, ct);
            if (entity == null)
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
            // 有范围时把范围谓词并进按主键取的那条查询，由数据库判定（与列表同一套语义）
            var scope = await ResolveScopeAsync(ct);
            var entities = scope == null
                ? await Repository.GetListAsync(idList, ct)
                : await Repository.ToListAsync(DataScopeExtensions.AndAlso(scope, e => idList.Contains(e.Id))!, ct);

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
    /// 本服务自己的行级数据范围谓词。默认返回 <c>null</c>（不额外限制）。override 以注入自定义可见性规则
    /// （例如「因为用户被指派到关联实体所以此行可见」），谓词会与容器里注册的
    /// <see cref="Tnzi.Data.IDataScopeProvider{TEntity}"/> 谓词 AND 后施加到查询与单行访问校验中。
    /// 它不检查新建或更新之后的实体（见类说明的「范围只约束读取」）。
    /// </summary>
    protected virtual Task<Expression<Func<TEntity, bool>>?> ApplyScopeAsync(CancellationToken cancellationToken)
        => Task.FromResult<Expression<Func<TEntity, bool>>?>(null);

    /// <summary>
    /// 判定一个<b>内存中的</b>实体是否满足生效的范围谓词（<see cref="ApplyScopeAsync"/> AND 注册的 provider）；无谓词时恒为 true。
    /// 用于写入约束：<see cref="BeforeCreateAsync"/> 收到的映射后实体、<see cref="AfterUpdateAsync"/> 时映射之后的实体。
    /// </summary>
    /// <remarks>
    /// 谓词编译后在内存中求值：它引用的导航必须已经加载在实体上，字符串按序号比较（不是数据库的排序规则）。
    /// 框架自己的取详情 / 更新 / 删除 / 批删<b>不</b>经过这里，那几处按主键 AND 范围谓词交给数据库判定。
    /// </remarks>
    protected virtual async Task<bool> IsInScopeAsync(TEntity entity, CancellationToken cancellationToken)
    {
        var scope = await ResolveScopeAsync(cancellationToken);
        return scope == null || scope.Compile()(entity);
    }

    /// <summary>
    /// 按主键加载一个落在范围内的实体；不存在或范围外返回 <c>null</c>（调用方一律答 404，避免存在性泄漏）。
    /// 范围谓词与主键一起交给数据库判定，和列表查询同一套 SQL 语义。
    /// </summary>
    private async Task<TEntity?> GetInScopeAsync(TKey id, CancellationToken cancellationToken)
    {
        var entity = await Repository.GetAsync(id, cancellationToken);
        if (entity == null)
            return null;

        var scope = await ResolveScopeAsync(cancellationToken);
        if (scope == null)
            return entity;

        TKey[] ids = [id];
        var inScope = await Repository.AnyAsync(DataScopeExtensions.AndAlso(scope, e => ids.Contains(e.Id)), cancellationToken);
        return inScope ? entity : null;
    }

    /// <summary>
    /// 生效的范围谓词：服务自己的 <see cref="ApplyScopeAsync"/> AND 容器里注册的全部
    /// <see cref="Tnzi.Data.IDataScopeProvider{TEntity}"/>。provider 不经覆写也生效：注册了却不被框架查询读取，
    /// 等于一个看起来已配置、实际放行全部行的授权规则。
    /// </summary>
    private async Task<Expression<Func<TEntity, bool>>?> ResolveScopeAsync(CancellationToken cancellationToken)
    {
        var own = await ApplyScopeAsync(cancellationToken);
        var registered = ServiceProvider.BuildDataScopeFilter<TEntity>();
        return DataScopeExtensions.AndAlso(own, registered);
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
