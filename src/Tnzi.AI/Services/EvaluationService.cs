namespace Tnzi.AI.Services;

/// <summary>
/// 评估运行管理服务实现
/// </summary>
public class EvaluationService : ApplicationService, IEvaluationService
{
    private readonly IRepository<EvaluationRun, Guid> _repository;

    public EvaluationService(IServiceProvider serviceProvider, IRepository<EvaluationRun, Guid> repository)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
    }

    public async Task<Result<EvaluationRunDetailDto>> GetByIdAsync(Guid id)
    {
        var entity = await _repository.GetAsync(id);
        if (entity == null)
            return Fail<EvaluationRunDetailDto>("Evaluation run not found", 404, ErrorCodes.EvaluationNotFound);

        return Ok(entity.MapTo<EvaluationRunDetailDto>());
    }

    public async Task<Result<IPagedList<EvaluationRunDto>>> GetListAsync(EvaluationRunQueryDto query)
    {
        Check.NotNull(query);

        var queryable = _repository
            .WhereIf(e => e.AgentId == query.AgentId!.Value, query.AgentId.HasValue)
            .WhereIf(e => e.Status == query.Status!.Value, query.Status.HasValue)
            .OrderByDescending(e => e.CreationTime);

        var pagedList = await queryable.ProjectTo<EvaluationRun, EvaluationRunDto>().CreateAsync(query);

        return Ok(pagedList);
    }

    public async Task<Result> DeleteAsync(Guid id)
    {
        var entity = await _repository.GetAsync(id);
        if (entity == null)
            return Fail("Evaluation run not found", 404, ErrorCodes.EvaluationNotFound);

        await _repository.DeleteAsync(entity);
        return Ok();
    }
}
