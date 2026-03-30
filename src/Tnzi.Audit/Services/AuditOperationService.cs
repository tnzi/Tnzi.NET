using AuditErrorCodes = Tnzi.Audit.Metadata.ErrorCodes;

namespace Tnzi.Audit.Services;

/// <summary>
/// 操作审计服务实现
/// </summary>
public class AuditOperationService : ApplicationService, IAuditOperationService
{
    private readonly IRepository<AuditOperation, Guid> _operationRepository;
    private readonly IAuditStore _auditStore;

    /// <summary>
    /// 初始化一个<see cref="AuditOperationService"/>类型的新实例
    /// </summary>
    public AuditOperationService(
        IRepository<AuditOperation, Guid> operationRepository,
        IAuditStore auditStore,
        IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        _operationRepository = Check.NotNull(operationRepository);
        _auditStore = Check.NotNull(auditStore);
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
            var functionNameLower = query.FunctionName.ToLower();
            queryable = queryable.Where(o => o.FunctionName.ToLower().Contains(functionNameLower));
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
        if (days <= 0)
            return Fail<int>("Days must be greater than 0", 400, AuditErrorCodes.AuditDeleteExpiredFailed);

        var count = await _auditStore.DeleteExpiredAsync(days);
        LogInformation("Deleted {Count} expired audit operations (older than {Days} days)", count, days);
        return Ok(count, $"Deleted {count} expired audit operations");
    }

    /// <summary>
    /// Export audit operations as CSV string
    /// </summary>
    public async Task<Result<string>> ExportToCsvAsync(AuditOperationQueryDto query, CancellationToken cancellationToken = default)
    {
        var operations = await GetFilteredOperationsAsync(query, cancellationToken);

        var sb = new StringBuilder();
        // CSV header
        sb.AppendLine("Id,FunctionName,UserName,Ip,HttpMethod,Url,HttpStatusCode,Elapsed,ResultType,Message,StartTime,EndTime,CreationTime");

        foreach (var op in operations)
        {
            sb.AppendLine(string.Join(",",
                EscapeCsv(op.Id.ToString()),
                EscapeCsv(op.FunctionName),
                EscapeCsv(op.UserName),
                EscapeCsv(op.Ip),
                EscapeCsv(op.HttpMethod),
                EscapeCsv(op.Url),
                op.HttpStatusCode?.ToString() ?? "",
                op.Elapsed.ToString(),
                op.ResultType.ToString(),
                EscapeCsv(op.Message),
                op.StartTime.ToString("o"),
                op.EndTime?.ToString("o") ?? "",
                op.CreationTime.ToString("o")
            ));
        }

        LogInformation("Exported {Count} audit operations to CSV", operations.Count);
        return Ok<string>(sb.ToString());
    }

    /// <summary>
    /// Export audit operations as JSON string
    /// </summary>
    public async Task<Result<string>> ExportToJsonAsync(AuditOperationQueryDto query, CancellationToken cancellationToken = default)
    {
        var operations = await GetFilteredOperationsAsync(query, cancellationToken);
        var dtos = operations.MapToList<AuditOperationDto>();

        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        var json = JsonSerializer.Serialize(dtos, jsonOptions);
        LogInformation("Exported {Count} audit operations to JSON", operations.Count);
        return Ok<string>(json);
    }

    /// <summary>
    /// Get filtered operations for export (reuses query logic, max 10000 rows for export)
    /// </summary>
    private async Task<List<AuditOperation>> GetFilteredOperationsAsync(AuditOperationQueryDto query, CancellationToken cancellationToken)
    {
        var queryable = _operationRepository.AsQueryable();

        if (!string.IsNullOrEmpty(query.FunctionName))
        {
            var functionNameLower = query.FunctionName.ToLower();
            queryable = queryable.Where(o => o.FunctionName.ToLower().Contains(functionNameLower));
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

        return await queryable
            .OrderByDescending(o => o.CreationTime)
            .Take(10000) // Export safety limit
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Escape a value for CSV output
    /// </summary>
    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        // If value contains comma, quote, or newline, wrap in quotes and escape inner quotes
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }

        return value;
    }

    /// <summary>
    /// 获取审计操作趋势统计
    /// </summary>
    public async Task<Result<List<AuditTrendPointDto>>> GetAuditTrendAsync(
        DateTime startDate,
        DateTime endDate,
        AuditTrendGroupBy groupBy = AuditTrendGroupBy.Daily,
        CancellationToken cancellationToken = default)
    {
        if (endDate <= startDate)
            return Fail<List<AuditTrendPointDto>>("End date must be after start date", 400);

        // 在数据库侧按日期分组聚合，避免加载全量记录到内存
        var dailyAggregates = await _operationRepository
            .Where(o => o.CreationTime >= startDate && o.CreationTime <= endDate)
            .GroupBy(o => o.CreationTime.Date)
            .Select(g => new
            {
                Date = g.Key,
                TotalCount = g.Count(),
                SuccessCount = g.Count(o => o.ResultType == AuditResultType.Success),
                FailedCount = g.Count(o => o.ResultType == AuditResultType.Failed),
                WarningCount = g.Count(o => o.ResultType == AuditResultType.Warning),
                AverageElapsed = g.Average(o => (double)o.Elapsed)
            })
            .OrderBy(g => g.Date)
            .ToListAsync(cancellationToken);

        // 按日直接返回；按周/月在已聚合的小数据集上二次分组
        List<AuditTrendPointDto> trend;
        if (groupBy == AuditTrendGroupBy.Daily)
        {
            trend = dailyAggregates.Select(g => new AuditTrendPointDto
            {
                Period = g.Date.ToString("yyyy-MM-dd"),
                TotalCount = g.TotalCount,
                SuccessCount = g.SuccessCount,
                FailedCount = g.FailedCount,
                WarningCount = g.WarningCount,
                AverageElapsed = g.AverageElapsed
            }).ToList();
        }
        else
        {
            trend = dailyAggregates
                .GroupBy(g => groupBy switch
                {
                    AuditTrendGroupBy.Weekly =>
                        $"{g.Date.Year}-W{CultureInfo.InvariantCulture.Calendar.GetWeekOfYear(g.Date, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday):D2}",
                    AuditTrendGroupBy.Monthly => g.Date.ToString("yyyy-MM"),
                    _ => g.Date.ToString("yyyy-MM-dd")
                })
                .OrderBy(g => g.Key)
                .Select(g =>
                {
                    var totalCount = g.Sum(d => d.TotalCount);
                    return new AuditTrendPointDto
                    {
                        Period = g.Key,
                        TotalCount = totalCount,
                        SuccessCount = g.Sum(d => d.SuccessCount),
                        FailedCount = g.Sum(d => d.FailedCount),
                        WarningCount = g.Sum(d => d.WarningCount),
                        AverageElapsed = totalCount > 0
                            ? g.Sum(d => d.AverageElapsed * d.TotalCount) / totalCount
                            : 0
                    };
                })
                .ToList();
        }

        return Ok(trend);
    }

    /// <summary>
    /// 获取 Top N 功能统计
    /// </summary>
    public async Task<Result<List<TopFunctionDto>>> GetTopFunctionsAsync(
        int topN = 10,
        DateTime? startDate = null,
        DateTime? endDate = null,
        CancellationToken cancellationToken = default)
    {
        if (topN <= 0)
            return Fail<List<TopFunctionDto>>("TopN must be greater than 0", 400);

        var queryable = _operationRepository.AsQueryable();

        if (startDate.HasValue)
            queryable = queryable.Where(o => o.CreationTime >= startDate.Value);
        if (endDate.HasValue)
            queryable = queryable.Where(o => o.CreationTime <= endDate.Value);

        // 在数据库侧分组聚合，避免加载全量记录到内存
        var topFunctions = await queryable
            .GroupBy(o => o.FunctionName)
            .Select(g => new TopFunctionDto
            {
                FunctionName = g.Key,
                HitCount = g.Count(),
                AverageElapsed = g.Average(o => (double)o.Elapsed),
                MaxElapsed = g.Max(o => o.Elapsed),
                ErrorCount = g.Count(o => o.ResultType == AuditResultType.Failed),
                ErrorRate = g.Count() > 0
                    ? (double)g.Count(o => o.ResultType == AuditResultType.Failed) / g.Count()
                    : 0
            })
            .OrderByDescending(f => f.HitCount)
            .Take(topN)
            .ToListAsync(cancellationToken);

        return Ok(topFunctions);
    }

    /// <summary>
    /// 获取 Top N 活跃用户统计
    /// </summary>
    public async Task<Result<List<TopUserDto>>> GetTopUsersAsync(
        int topN = 10,
        DateTime? startDate = null,
        DateTime? endDate = null,
        CancellationToken cancellationToken = default)
    {
        if (topN <= 0)
            return Fail<List<TopUserDto>>("TopN must be greater than 0", 400);

        var queryable = _operationRepository.AsQueryable();

        if (startDate.HasValue)
            queryable = queryable.Where(o => o.CreationTime >= startDate.Value);
        if (endDate.HasValue)
            queryable = queryable.Where(o => o.CreationTime <= endDate.Value);

        // 在数据库侧分组聚合，避免加载全量记录到内存
        var topUsers = await queryable
            .Where(o => o.UserId != null)
            .GroupBy(o => new { o.UserId, o.UserName })
            .Select(g => new TopUserDto
            {
                UserId = g.Key.UserId!.Value,
                UserName = g.Key.UserName,
                OperationCount = g.Count(),
                SuccessCount = g.Count(o => o.ResultType == AuditResultType.Success),
                FailedCount = g.Count(o => o.ResultType == AuditResultType.Failed),
                SuccessRate = g.Count() > 0
                    ? (double)g.Count(o => o.ResultType == AuditResultType.Success) / g.Count()
                    : 0
            })
            .OrderByDescending(u => u.OperationCount)
            .Take(topN)
            .ToListAsync(cancellationToken);

        return Ok(topUsers);
    }

    /// <summary>
    /// 在数据库侧计算统计信息
    /// </summary>
    private async Task<AuditOperationStatistics> CalculateStatisticsAsync(IQueryable<AuditOperation> query)
    {
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
            return new AuditOperationStatistics();

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
