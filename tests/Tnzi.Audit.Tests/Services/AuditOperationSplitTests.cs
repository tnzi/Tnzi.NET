
namespace Tnzi.Audit.Tests.Services;

/// <summary>
/// Logs / Operations 语义分流测试 — IsWriteOperation / HttpMethod 查询过滤。
/// Logs 视图（IsWriteOperation=null）返回全部请求级审计；
/// Operations 视图（IsWriteOperation=true）仅返回 POST/PUT/PATCH/DELETE 变更类操作，
/// 且按 query-via-POST 惯例排除路径以 "/query" 结尾的 POST 列表查询（纯读）。
/// </summary>
public class AuditOperationSplitTests
{
    private readonly Mock<IRepository<AuditOperation, Guid>> _repositoryMock;
    private readonly Mock<IAuditStore> _auditStoreMock;
    private readonly Mock<IOptionsMonitor<AuditOptions>> _optionsMonitorMock;
    private readonly Mock<IServiceProvider> _serviceProviderMock;
    private readonly AuditOperationService _service;

    public AuditOperationSplitTests()
    {
        var config = new TypeAdapterConfig();
        var mapper = new Mapper(config);
        MapperExtensions.SetMapper(mapper);

        _repositoryMock = new Mock<IRepository<AuditOperation, Guid>>();
        _auditStoreMock = new Mock<IAuditStore>();
        _optionsMonitorMock = new Mock<IOptionsMonitor<AuditOptions>>();
        _optionsMonitorMock.Setup(x => x.CurrentValue).Returns(new AuditOptions());
        _serviceProviderMock = new Mock<IServiceProvider>();
        _serviceProviderMock
            .Setup(x => x.GetService(typeof(Microsoft.Extensions.Logging.ILoggerFactory)))
            .Returns(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
        _service = new AuditOperationService(_repositoryMock.Object, _auditStoreMock.Object, _optionsMonitorMock.Object, _serviceProviderMock.Object);
    }

    private void SetupOperationQueryable(List<AuditOperation> operations)
    {
        var mock = operations.BuildMock();
        _repositoryMock.Setup(r => r.AsQueryable()).Returns(mock);
        _repositoryMock.As<IQueryable<AuditOperation>>().Setup(q => q.Provider).Returns(mock.Provider);
        _repositoryMock.As<IQueryable<AuditOperation>>().Setup(q => q.Expression).Returns(mock.Expression);
        _repositoryMock.As<IQueryable<AuditOperation>>().Setup(q => q.ElementType).Returns(mock.ElementType);
        _repositoryMock.As<IQueryable<AuditOperation>>().Setup(q => q.GetEnumerator()).Returns(mock.GetEnumerator());
    }

    private static AuditOperation CreateOperation(string? httpMethod, string functionName = "Test.Action", string? url = "/api/test") => new()
    {
        Id = Guid.NewGuid(),
        FunctionName = functionName,
        HttpMethod = httpMethod,
        Url = url,
        ResultType = AuditResultType.Success,
        StartTime = DateTime.UtcNow,
        CreationTime = DateTime.UtcNow
    };

    private static List<AuditOperation> MixedOperations() =>
    [
        CreateOperation("GET", "Users.GetProfile"),
        CreateOperation("GET", "Orders.GetList"),
        CreateOperation("HEAD", "Health.Check"),
        CreateOperation("POST", "Users.Create"),
        CreateOperation("PUT", "Users.Update"),
        CreateOperation("PATCH", "Orders.Patch"),
        CreateOperation("DELETE", "Users.Delete"),
        CreateOperation(null, "Legacy.NoMethod")
    ];

    /// <summary>
    /// 伪读 POST 惯例样本：框架列表查询是 POST .../query（纯读），
    /// Get* 控制器方法也可能经 POST .../list、.../summary 等路径暴露（同为纯读），
    /// 其余 POST/PUT 是真实写操作。
    /// </summary>
    private static List<AuditOperation> QueryConventionOperations() =>
    [
        CreateOperation("GET", "Agents.Get", "/api/admin/agents/1"),
        CreateOperation("POST", "Agents.GetList", "/api/admin/agents/query"),
        CreateOperation("POST", "Threads.GetList", "/api/admin/threads/Query"),
        CreateOperation("POST", "Orders.GetList", "/api/admin/orders/query?pageIndex=1&pageSize=20"),
        CreateOperation("POST", "Users.GetList", "/api/admin/users/list"),
        CreateOperation("POST", "Usage.GetSummary", "/api/admin/ai/usage/summary"),
        CreateOperation("POST", "Agents.Create", "/api/admin/agents"),
        CreateOperation("POST", "Agents.Export", "/api/admin/agents/query-export"),
        CreateOperation("PUT", "Agents.Update", "/api/admin/agents/1"),
        CreateOperation("POST", "Legacy.NoUrl", null)
    ];

    [Fact]
    public async Task GetOperationsAsync_IsWriteOperationTrue_ExcludesQueryViaPostConvention()
    {
        SetupOperationQueryable(QueryConventionOperations());

        var result = await _service.GetOperationsAsync(new AuditOperationQueryDto
        {
            IsWriteOperation = true,
            PageIndex = 1,
            PageSize = 50
        });

        result.Succeeded.ShouldBeTrue(result.Message);
        // Agents.Create + Agents.Export(query-export 不以 /query 结尾、非 .Get 名) + Agents.Update
        // + Legacy.NoUrl(POST 无 Url 且非 .Get 名，保守归入写)；
        // Users.GetList(/list) 与 Usage.GetSummary(/summary) 经 ".Get" 段排除。
        result.Data!.TotalCount.ShouldBe(4);
        result.Data.Items.Select(o => o.FunctionName)
            .ShouldBe(["Agents.Create", "Agents.Export", "Agents.Update", "Legacy.NoUrl"], ignoreOrder: true);
    }

    [Fact]
    public async Task GetOperationsAsync_IsWriteOperationFalse_IncludesQueryViaPostConvention()
    {
        SetupOperationQueryable(QueryConventionOperations());

        var result = await _service.GetOperationsAsync(new AuditOperationQueryDto
        {
            IsWriteOperation = false,
            PageIndex = 1,
            PageSize = 50
        });

        result.Succeeded.ShouldBeTrue(result.Message);
        // GET + /query 结尾（大小写不敏感）×2 + /query?qs 中缀 + .Get 名经 /list、/summary 暴露 ×2
        result.Data!.TotalCount.ShouldBe(6);
        result.Data.Items.Select(o => o.FunctionName)
            .ShouldBe(
                ["Agents.Get", "Agents.GetList", "Threads.GetList", "Orders.GetList", "Users.GetList", "Usage.GetSummary"],
                ignoreOrder: true);
    }

    [Fact]
    public async Task GetOperationsAsync_IsWriteOperationNull_StillReturnsAllIncludingQueryViaPost()
    {
        SetupOperationQueryable(QueryConventionOperations());

        var result = await _service.GetOperationsAsync(new AuditOperationQueryDto
        {
            PageIndex = 1,
            PageSize = 50
        });

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.TotalCount.ShouldBe(10);
    }

    /// <summary>
    /// 新旧行混布：采集时定案的 IsWrite 列优先于启发式（能纠正启发式必然误判的样本），
    /// IsWrite=null 的历史行回退旧启发式。
    /// </summary>
    private static List<AuditOperation> StoredClassificationOperations()
    {
        var storedRead = CreateOperation("POST", "Staff.OperationsSection", "/api/admin/staff-profiles/operations-section");
        storedRead.IsWrite = false;    // 启发式会判写（POST 非 /query 非 .Get），存量列定案为读

        var storedWrite = CreateOperation("GET", "Jobs.Trigger", "/api/admin/jobs/trigger");
        storedWrite.IsWrite = true;    // 启发式会判读（GET），存量列定案为写

        return
        [
            storedRead,
            storedWrite,
            CreateOperation("POST", "Legacy.Create", "/api/legacy"),        // null → 回退启发式=写
            CreateOperation("POST", "Legacy.GetList", "/api/legacy/query")  // null → 回退启发式=读
        ];
    }

    [Fact]
    public async Task GetOperationsAsync_WriteView_PrefersStoredIsWrite_AndFallsBackForLegacyRows()
    {
        SetupOperationQueryable(StoredClassificationOperations());

        var result = await _service.GetOperationsAsync(new AuditOperationQueryDto
        {
            IsWriteOperation = true,
            PageIndex = 1,
            PageSize = 50
        });

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.Items.Select(o => o.FunctionName)
            .ShouldBe(["Jobs.Trigger", "Legacy.Create"], ignoreOrder: true);
    }

    [Fact]
    public async Task GetOperationsAsync_ReadView_PrefersStoredIsWrite_AndFallsBackForLegacyRows()
    {
        SetupOperationQueryable(StoredClassificationOperations());

        var result = await _service.GetOperationsAsync(new AuditOperationQueryDto
        {
            IsWriteOperation = false,
            PageIndex = 1,
            PageSize = 50
        });

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.Items.Select(o => o.FunctionName)
            .ShouldBe(["Staff.OperationsSection", "Legacy.GetList"], ignoreOrder: true);
    }

    [Fact]
    public async Task GetOperationsAsync_IsWriteOperationTrue_ReturnsOnlyWriteMethods()
    {
        SetupOperationQueryable(MixedOperations());

        var result = await _service.GetOperationsAsync(new AuditOperationQueryDto
        {
            IsWriteOperation = true,
            PageIndex = 1,
            PageSize = 50
        });

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.TotalCount.ShouldBe(4);
        result.Data.Items.Select(o => o.HttpMethod)
            .ShouldAllBe(m => m == "POST" || m == "PUT" || m == "PATCH" || m == "DELETE");
    }

    [Fact]
    public async Task GetOperationsAsync_IsWriteOperationFalse_ReturnsOnlyReadRequests()
    {
        SetupOperationQueryable(MixedOperations());

        var result = await _service.GetOperationsAsync(new AuditOperationQueryDto
        {
            IsWriteOperation = false,
            PageIndex = 1,
            PageSize = 50
        });

        result.Succeeded.ShouldBeTrue();
        // GET ×2 + HEAD + null-method legacy row
        result.Data!.TotalCount.ShouldBe(4);
        result.Data.Items.ShouldAllBe(o =>
            o.HttpMethod != "POST" && o.HttpMethod != "PUT" && o.HttpMethod != "PATCH" && o.HttpMethod != "DELETE");
    }

    [Fact]
    public async Task GetOperationsAsync_IsWriteOperationNull_ReturnsAllRequests()
    {
        SetupOperationQueryable(MixedOperations());

        var result = await _service.GetOperationsAsync(new AuditOperationQueryDto
        {
            PageIndex = 1,
            PageSize = 50
        });

        result.Succeeded.ShouldBeTrue();
        result.Data!.TotalCount.ShouldBe(8);
    }

    [Fact]
    public async Task GetOperationsAsync_HttpMethodFilter_IsExactAndCaseInsensitive()
    {
        SetupOperationQueryable(MixedOperations());

        var result = await _service.GetOperationsAsync(new AuditOperationQueryDto
        {
            HttpMethod = "post",
            PageIndex = 1,
            PageSize = 50
        });

        result.Succeeded.ShouldBeTrue();
        result.Data!.TotalCount.ShouldBe(1);
        result.Data.Items[0].HttpMethod.ShouldBe("POST");
    }

    [Fact]
    public async Task GetOperationsAsync_IsWriteOperation_ComposesWithOtherFilters()
    {
        SetupOperationQueryable(MixedOperations());

        var result = await _service.GetOperationsAsync(new AuditOperationQueryDto
        {
            IsWriteOperation = true,
            FunctionName = "users",
            PageIndex = 1,
            PageSize = 50
        });

        result.Succeeded.ShouldBeTrue();
        // Users.Create / Users.Update / Users.Delete (Users.GetProfile is a GET)
        result.Data!.TotalCount.ShouldBe(3);
    }

    [Fact]
    public async Task ExportToCsvAsync_HonorsIsWriteOperationFilter()
    {
        SetupOperationQueryable(MixedOperations());

        var result = await _service.ExportToCsvAsync(new AuditOperationQueryDto
        {
            IsWriteOperation = true,
            PageIndex = 1,
            PageSize = 50
        });

        result.Succeeded.ShouldBeTrue();
        result.Data!.ShouldContain("Users.Create");
        result.Data.ShouldNotContain("Users.GetProfile");
        result.Data.ShouldNotContain("Legacy.NoMethod");
    }

    [Fact]
    public async Task ExportToCsvAsync_FormulaLikeUserControlledFields_AreEscaped()
    {
        // Url/UserName 是用户可控字段,以公式起始字符开头时经核心 CsvBuilder 必须前置单引号防注入
        var op = CreateOperation("POST", "Users.Create", "=HYPERLINK(\"http://evil\")");
        op.UserName = "-cmd|calc";
        SetupOperationQueryable([op]);

        var result = await _service.ExportToCsvAsync(new AuditOperationQueryDto { PageIndex = 1, PageSize = 50 });

        result.Succeeded.ShouldBeTrue();
        result.Data!.ShouldContain("'=HYPERLINK");
        result.Data.ShouldContain("'-cmd|calc");
        result.Data.ShouldNotContain(",=HYPERLINK");
    }
}
