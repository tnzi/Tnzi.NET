namespace Tnzi.Audit.Tests.Services;

/// <summary>
/// 审计导出对「结果超过上限」的处置。
/// </summary>
/// <remarks>
/// 缺陷形态：导出静默 <c>Take(10000)</c>，产物是一份看起来完整的文件 —— 没有「已截断」标记、没有告警，
/// 日志里那句「Exported N」只是重复被截断后的数字。审计导出是证据类产物，「我导出了那段时间的全部记录」
/// 与「前 10000 条」在合规场景里不是一回事。修法是<b>拒绝而不是截断</b>：超限返回失败并说明怎么收窄。
/// </remarks>
public class AuditExportLimitTests
{
    private readonly Mock<IRepository<AuditOperation, Guid>> _repositoryMock = new();
    private readonly Mock<IAuditStore> _auditStoreMock = new();
    private readonly Mock<IOptionsMonitor<AuditOptions>> _optionsMonitorMock = new();
    private readonly AuditOptions _options = new();
    private readonly AuditOperationService _service;

    public AuditExportLimitTests()
    {
        MapperExtensions.SetMapper(new Mapper(new TypeAdapterConfig()));

        _optionsMonitorMock.Setup(x => x.CurrentValue).Returns(() => _options);
        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider
            .Setup(x => x.GetService(typeof(Microsoft.Extensions.Logging.ILoggerFactory)))
            .Returns(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
        _service = new AuditOperationService(_repositoryMock.Object, _auditStoreMock.Object, _optionsMonitorMock.Object, serviceProvider.Object);
    }

    private void SetupOperations(int count)
    {
        var operations = Enumerable.Range(0, count).Select(i => new AuditOperation
        {
            Id = Guid.NewGuid(),
            FunctionName = $"Users.Action{i}",
            HttpMethod = "POST",
            Url = "/api/users",
            ResultType = AuditResultType.Success,
            StartTime = DateTime.UtcNow.AddSeconds(-i),
            CreationTime = DateTime.UtcNow.AddSeconds(-i)
        }).ToList();

        var mock = operations.BuildMock();
        _repositoryMock.Setup(r => r.AsQueryable()).Returns(mock);
        _repositoryMock.As<IQueryable<AuditOperation>>().Setup(q => q.Provider).Returns(mock.Provider);
        _repositoryMock.As<IQueryable<AuditOperation>>().Setup(q => q.Expression).Returns(mock.Expression);
        _repositoryMock.As<IQueryable<AuditOperation>>().Setup(q => q.ElementType).Returns(mock.ElementType);
        _repositoryMock.As<IQueryable<AuditOperation>>().Setup(q => q.GetEnumerator()).Returns(() => mock.GetEnumerator());
    }

    /// <summary>★现形用例：默认上限之上，此前拿到的是一份被安静砍成 10000 行的文件。</summary>
    [Fact]
    public async Task Csv_export_refuses_instead_of_silently_truncating_at_the_default_limit()
    {
        SetupOperations(10_001);

        var result = await _service.ExportToCsvAsync(new AuditOperationQueryDto());

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message!.ShouldContain("10000");
        result.Message!.ShouldContain("narrow", Case.Insensitive);
    }

    [Fact]
    public async Task Json_export_refuses_too()
    {
        SetupOperations(10_001);

        var result = await _service.ExportToJsonAsync(new AuditOperationQueryDto());

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    [Fact]
    public async Task Exactly_at_the_limit_still_exports_everything()
    {
        SetupOperations(3);
        _options.ExportMaxRows = 3;

        var result = await _service.ExportToCsvAsync(new AuditOperationQueryDto());

        result.Succeeded.ShouldBeTrue();
        // 表头 + 3 行数据
        result.Data!.TrimEnd().Split('\n').Length.ShouldBe(4);
    }

    [Fact]
    public async Task The_limit_is_configurable_and_the_message_names_the_configured_value()
    {
        SetupOperations(3);
        _options.ExportMaxRows = 2;

        var result = await _service.ExportToCsvAsync(new AuditOperationQueryDto());

        result.Succeeded.ShouldBeFalse();
        result.Message!.ShouldContain("2");
        result.ErrorCode.ShouldBe(Tnzi.Audit.Metadata.ErrorCodes.AuditExportTooLarge);
    }
}
