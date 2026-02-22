using AuditErrorCodes = Tnzi.Audit.Metadata.ErrorCodes;

namespace Tnzi.Audit.Services;

/// <summary>
/// 操作审计服务实现
/// </summary>
public class AuditOperationService : ApplicationService, IAuditOperationService
{
    private readonly IRepository<AuditOperation, Guid> _operationRepository;

    /// <summary>
    /// 初始化一个<see cref="AuditOperationService"/>类型的新实例
    /// </summary>
    public AuditOperationService(
        IRepository<AuditOperation, Guid> operationRepository,
        IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        _operationRepository = Check.NotNull(operationRepository);
    }

    /// <summary>
    /// 获取操作审计
    /// </summary>
    public async Task<Result<AuditOperationDto>> GetAsync(Guid id)
    {
        var operation = await _operationRepository
            .Where(o => o.Id == id)
            .Include(o => o.EntityEntries)
                .ThenInclude(e => e.PropertyEntries)
            .FirstOrDefaultAsync();
        if (operation == null)
        {
            return Fail<AuditOperationDto>("Audit operation not found", 404, AuditErrorCodes.AuditOperationNotFound);
        }
        return Ok(operation.MapTo<AuditOperationDto>());
    }

    /// <summary>
    /// 获取用户的操作审计列表
    /// </summary>
    public async Task<Result<IEnumerable<AuditOperationDto>>> GetUserOperationsAsync(
        Guid userId,
        DateTime? startDate = null,
        DateTime? endDate = null,
        AuditResultType? resultType = null)
    {
        var query = _operationRepository.Where(o => o.UserId == userId);

        if (startDate.HasValue)
        {
            query = query.Where(o => o.CreationTime >= startDate.Value);
        }

        if (endDate.HasValue)
        {
            query = query.Where(o => o.CreationTime <= endDate.Value);
        }

        if (resultType.HasValue)
        {
            query = query.Where(o => o.ResultType == resultType.Value);
        }

        var operations = await query
            .OrderByDescending(o => o.CreationTime)
            .Take(500) // 分页保护
            .ToListAsync();
        return Ok(operations.MapToList<AuditOperationDto>().AsEnumerable());
    }

    /// <summary>
    /// 获取操作审计列表（分页）
    /// </summary>
    public async Task<Result<IPagedList<AuditOperationDto>>> GetOperationsAsync(AuditOperationQueryDto query)
    {
        var queryable = _operationRepository.AsQueryable();

        if (!string.IsNullOrEmpty(query.FunctionName))
        {
            queryable = queryable.Where(o => o.FunctionName.Contains(query.FunctionName));
        }

        if (!string.IsNullOrEmpty(query.PermissionName))
        {
            queryable = queryable.Where(o => o.PermissionName == query.PermissionName);
        }

        if (query.UserId.HasValue)
        {
            queryable = queryable.Where(o => o.UserId == query.UserId.Value);
        }

        if (query.ResultType.HasValue)
        {
            queryable = queryable.Where(o => o.ResultType == query.ResultType.Value);
        }

        if (query.StartDate.HasValue)
        {
            queryable = queryable.Where(o => o.CreationTime >= query.StartDate.Value);
        }

        if (query.EndDate.HasValue)
        {
            queryable = queryable.Where(o => o.CreationTime <= query.EndDate.Value);
        }

        if (!string.IsNullOrEmpty(query.Ip))
        {
            queryable = queryable.Where(o => o.Ip == query.Ip);
        }

        queryable = queryable.OrderByDescending(o => o.CreationTime);

        var paged = await queryable.CreateAsync(query.PageIndex, query.PageSize);
        var dtoItems = paged.Items.MapToList<AuditOperationDto>();
        var dtoPaged = new PagedList<AuditOperationDto>(dtoItems, paged.PageIndex, paged.PageSize, paged.TotalCount);

        return Ok((IPagedList<AuditOperationDto>)dtoPaged);
    }

    /// <summary>
    /// 获取功能的操作统计
    /// </summary>
    public async Task<Result<AuditOperationStatistics>> GetFunctionStatisticsAsync(
        string functionName,
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        var query = _operationRepository.Where(o => o.FunctionName == functionName);

        if (startDate.HasValue)
        {
            query = query.Where(o => o.CreationTime >= startDate.Value);
        }

        if (endDate.HasValue)
        {
            query = query.Where(o => o.CreationTime <= endDate.Value);
        }

        var statistics = await CalculateStatisticsAsync(query);
        return Ok(statistics);
    }

    /// <summary>
    /// 获取用户的操作统计
    /// </summary>
    public async Task<Result<AuditOperationStatistics>> GetUserStatisticsAsync(
        Guid userId,
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        var query = _operationRepository.Where(o => o.UserId == userId);

        if (startDate.HasValue)
        {
            query = query.Where(o => o.CreationTime >= startDate.Value);
        }

        if (endDate.HasValue)
        {
            query = query.Where(o => o.CreationTime <= endDate.Value);
        }

        var statistics = await CalculateStatisticsAsync(query);
        return Ok(statistics);
    }

    /// <summary>
    /// 删除过期操作审计
    /// </summary>
    public async Task<Result<int>> DeleteExpiredOperationsAsync(int days = 90)
    {
        var expireDate = DateTime.UtcNow.AddDays(-days);

        var count = await _operationRepository.CountAsync(o => o.CreationTime < expireDate);
        if (count > 0)
        {
            await _operationRepository.DeleteAsync(o => o.CreationTime < expireDate);
        }

        LogInformation("Deleted {Count} expired audit operations (older than {Days} days)", count, days);
        return Ok(count, $"Deleted {count} expired audit operations");
    }

    /// <summary>
    /// 在数据库侧计算统计信息
    /// </summary>
    private async Task<AuditOperationStatistics> CalculateStatisticsAsync(IQueryable<AuditOperation> query)
    {
        if (!await query.AnyAsync())
        {
            return new AuditOperationStatistics();
        }

        var stats = await query.GroupBy(o => 1).Select(g => new
        {
            TotalCount = g.Count(),
            SuccessCount = g.Count(o => o.ResultType == AuditResultType.Success),
            FailedCount = g.Count(o => o.ResultType == AuditResultType.Failed),
            WarningCount = g.Count(o => o.ResultType == AuditResultType.Warning),
            AverageElapsed = g.Average(o => (double)o.Elapsed),
            MaxElapsed = g.Max(o => o.Elapsed),
            MinElapsed = g.Min(o => o.Elapsed)
        }).FirstOrDefaultAsync();

        if (stats == null)
        {
            return new AuditOperationStatistics();
        }

        return new AuditOperationStatistics
        {
            TotalCount = stats.TotalCount,
            SuccessCount = stats.SuccessCount,
            FailedCount = stats.FailedCount,
            WarningCount = stats.WarningCount,
            AverageElapsed = stats.AverageElapsed,
            MaxElapsed = stats.MaxElapsed,
            MinElapsed = stats.MinElapsed
        };
    }
}
