
namespace Tnzi.Audit.Tests.Services;

/// <summary>
/// AuditOperationService 单元测试（构造契约）。
/// </summary>
/// <remarks>
/// ★ <c>GetUserOperationsAsync</c> 与 <c>GetFunctionStatisticsAsync</c> <b>没有被覆盖</b>：
/// 它们要求对 <c>IRepository</c>（继承 <c>IQueryable</c>）的查询谓词求值，mock 做不到，
/// 只能跑真实 provider 的集成测试。此前这两条以<b>空方法体的 <c>[Fact]</c></b> 存在 ——
/// 无条件通过、计入测试数、什么都不断言，比没有测试更糟：它让覆盖看起来是有的。
/// 现已删除；要补就补集成测试。
/// </remarks>
public class AuditOperationServiceTests
{
    private readonly Mock<IRepository<AuditOperation, Guid>> _repositoryMock;
    private readonly Mock<IAuditStore> _auditStoreMock;
    private readonly Mock<IOptionsMonitor<AuditOptions>> _optionsMonitorMock;
    private readonly Mock<IServiceProvider> _serviceProviderMock;

    public AuditOperationServiceTests()
    {
        _repositoryMock = new Mock<IRepository<AuditOperation, Guid>>();
        _auditStoreMock = new Mock<IAuditStore>();
        _optionsMonitorMock = new Mock<IOptionsMonitor<AuditOptions>>();
        _optionsMonitorMock.Setup(x => x.CurrentValue).Returns(new AuditOptions());
        _serviceProviderMock = new Mock<IServiceProvider>();
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_Should_Throw_When_Repository_Is_Null()
    {
        // Act & Assert
        Should.Throw<ArgumentNullException>(() => new AuditOperationService(null!, _auditStoreMock.Object, _optionsMonitorMock.Object, _serviceProviderMock.Object));
    }

    [Fact]
    public void Constructor_Should_Initialize_Successfully()
    {
        // Act
        var service = new AuditOperationService(_repositoryMock.Object, _auditStoreMock.Object, _optionsMonitorMock.Object, _serviceProviderMock.Object);

        // Assert
        service.ShouldNotBeNull();
    }

    #endregion
}
