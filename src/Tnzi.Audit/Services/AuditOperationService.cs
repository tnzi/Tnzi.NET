using AuditErrorCodes = Tnzi.Audit.Metadata.ErrorCodes;

namespace Tnzi.Audit.Services;

/// <summary>
/// 操作审计服务实现
/// </summary>
public class AuditOperationService : ApplicationService, IAuditOperationService
{
    private readonly IRepository<AuditOperation, Guid> _operationRepository;
    private readonly IAuditStore _auditStore;
    private readonly IOptionsMonitor<AuditOptions> _optionsMonitor;

    private AuditOptions Options => _optionsMonitor.CurrentValue;

    /// <summary>
    /// 初始化一个<see cref="AuditOperationService"/>类型的新实例
    /// </summary>
    public AuditOperationService(
        IRepository<AuditOperation, Guid> operationRepository,
        IAuditStore auditStore,
        IOptionsMonitor<AuditOptions> optionsMonitor,
        IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        _operationRepository = Check.NotNull(operationRepository);
        _auditStore = Check.NotNull(auditStore);
        _optionsMonitor = Check.NotNull(optionsMonitor);
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
    /// 变更类（写操作）HTTP 方法集合 - Logs/Operations 语义分流的判别依据。
    /// AuditMiddleware 写入的 HttpMethod 为大写（context.Request.Method）。
    /// </summary>
    private static readonly string[] WriteHttpMethods = ["POST", "PUT", "PATCH", "DELETE"];

    /// <summary>
    /// 应用查询过滤条件（GetOperationsAsync 与导出共用）。
    /// </summary>
    /// <remarks>
    /// Logs / Operations 语义分流在查询端实现：写入端是单一管道、单一表
    /// （AuditMiddleware → Audit_Operation），两个 admin 视图读取同一存储 -
    /// 若在写入端丢弃 GET 类请求，则请求级审计日志（Logs 视图）会丢失数据。
    /// Operations 视图通过 <see cref="AuditOperationQueryDto.IsWriteOperation"/>=true
    /// 过滤出变更类记录。
    /// <para>
    /// 分类来源分两代：新行以采集时定案的 <c>AuditOperation.IsWrite</c> 列为准
    /// （AuditOperationClassifier：[AuditRead] &gt; 方法级操作权限码 &gt;
    /// 三层门约定 admin 面（类级 .view）无操作码=读 &gt; HTTP 方法+伪读启发式）；<c>IsWrite=null</c> 的
    /// 历史行回退旧的查询时启发式 - (1) query-via-POST 列表查询惯例
    /// （<c>POST .../query</c>，Url 存 Path + QueryString，匹配 "/query" 结尾与
    /// "/query?" 中缀）；(2) 约定无副作用的 <c>Get*</c> 控制器方法
    /// （FunctionName 按 ".Get" 段判别）。
    /// </para>
    /// </remarks>
    private static IQueryable<AuditOperation> ApplyQueryFilters(IQueryable<AuditOperation> queryable, AuditOperationQueryDto query)
    {
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

        if (!string.IsNullOrEmpty(query.HttpMethod))
        {
            var method = query.HttpMethod.ToUpper();
            queryable = queryable.Where(o => o.HttpMethod != null && o.HttpMethod.ToUpper() == method);
        }

        if (query.IsWriteOperation.HasValue)
        {
            // 采集时定案的 IsWrite 列优先（AuditOperationClassifier：[AuditRead] > 方法级
            // 操作权限码 > admin 面无操作码=读 > HTTP 方法+伪读启发式）。
            // IsWrite=null 的历史行（列引入前）回退旧的查询时启发式：
            // 写方法 + 非"伪读 POST"（/query 路径惯例、.Get 方法名）才算写操作。
            queryable = query.IsWriteOperation.Value
                ? queryable.Where(o => o.IsWrite != null
                    ? o.IsWrite == true
                    : o.HttpMethod != null
                        && WriteHttpMethods.Contains(o.HttpMethod.ToUpper())
                        && !(o.HttpMethod.ToUpper() == "POST"
                            && ((o.Url != null && (o.Url.ToLower().EndsWith("/query") || o.Url.ToLower().Contains("/query?")))
                                || o.FunctionName.Contains(".Get"))))
                : queryable.Where(o => o.IsWrite != null
                    ? o.IsWrite == false
                    : o.HttpMethod == null
                        || !WriteHttpMethods.Contains(o.HttpMethod.ToUpper())
                        || (o.HttpMethod.ToUpper() == "POST"
                            && ((o.Url != null && (o.Url.ToLower().EndsWith("/query") || o.Url.ToLower().Contains("/query?")))
                                || o.FunctionName.Contains(".Get"))));
        }

        return queryable;
    }

    /// <summary>
    /// 获取操作审计列表（分页）
    /// </summary>
    public async Task<Result<IPagedList<AuditOperationDto>>> GetOperationsAsync(AuditOperationQueryDto query)
    {
        var queryable = ApplyQueryFilters(_operationRepository.AsQueryable(), query)
            .OrderByDescending(o => o.CreationTime);

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
    public async Task<Result<int>> DeleteExpiredOperationsAsync(int? days = null)
    {
        var retentionDays = days ?? Options.RetentionDays;
        if (retentionDays <= 0)
            return Fail<int>("Days must be greater than 0", 400, AuditErrorCodes.AuditDeleteExpiredFailed);

        var count = await _auditStore.DeleteExpiredAsync(retentionDays);
        LogInformation("Deleted {Count} expired audit operations (older than {Days} days)", count, retentionDays);
        return Ok(count, $"Deleted {count} expired audit operations");
    }

    /// <summary>
    /// Export audit operations as CSV string
    /// </summary>
    public async Task<Result<string>> ExportToCsvAsync(AuditOperationQueryDto query, CancellationToken cancellationToken = default)
    {
        var operations = await GetFilteredOperationsAsync(query, cancellationToken);

        // 单元格转义统一走核心 CsvBuilder(含公式注入防护,Url/Message 等用户可控字段必须防护)
        var csv = new CsvBuilder();
        csv.AppendRow("Id", "FunctionName", "UserName", "Ip", "HttpMethod", "Url", "HttpStatusCode", "Elapsed", "ResultType", "Message", "StartTime", "EndTime", "CreationTime");

        foreach (var op in operations)
        {
            csv.AppendRow(op.Id, op.FunctionName, op.UserName, op.Ip, op.HttpMethod, op.Url,
                op.HttpStatusCode, op.Elapsed, op.ResultType, op.Message,
                op.StartTime, op.EndTime, op.CreationTime);
        }

        LogInformation("Exported {Count} audit operations to CSV", operations.Count);
        return Ok<string>(csv.ToString());
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
        return await ApplyQueryFilters(_operationRepository.AsQueryable(), query)
            .OrderByDescending(o => o.CreationTime)
            .Take(10000) // Export safety limit
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// 获取审计操作趋势统计
    /// </summary>
    public async Task<Result<List<AuditTrendPointDto>>> GetAuditTrendAsync(
        DateTime startDate,
        DateTime endDate,
        TrendInterval groupBy = TrendInterval.Daily,
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

        // 分桶标签统一走核心 TimeBucket（周口径 = ISO 8601，标签用 ISO 周年而非日历年）。
        // 按日也走同一条路径：此前按日与按周/月分两支实现，格式化各写一遍。
        var trend = dailyAggregates
            .GroupBy(g => TimeBucket.Label(g.Date, groupBy))
            .OrderBy(g => g.Key, StringComparer.Ordinal)
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
