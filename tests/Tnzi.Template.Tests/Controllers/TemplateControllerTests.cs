
namespace Tnzi.Template.Tests.Controllers;

/// <summary>
/// 模板控制器测试类
/// </summary>
public class TemplateControllerTests
{
    private readonly Mock<ITemplateStoreService> _templateStoreServiceMock;
    private readonly TestTemplateController _controller;

    public TemplateControllerTests()
    {
        // 初始化 Mapster
        var config = new TypeAdapterConfig();
        var mapper = new Mapper(config);
        MapperExtensions.SetMapper(mapper);

        _templateStoreServiceMock = new Mock<ITemplateStoreService>();

        _controller = new TestTemplateController(
            _templateStoreServiceMock.Object);

        // 设置 HTTP 上下文和服务提供者
        var httpContext = new DefaultHttpContext();
        var serviceProvider = new Mock<IServiceProvider>();
        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>()))
            .Returns(new Mock<ILogger>().Object);
        serviceProvider.Setup(sp => sp.GetService(typeof(ILoggerFactory)))
            .Returns(loggerFactory.Object);
        httpContext.RequestServices = serviceProvider.Object;
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };
    }

    #region GetById Tests

    [Fact]
    public async Task GetById_WithValidId_ReturnsTemplate()
    {
        // Arrange
        var id = Guid.NewGuid();
        var dto = new TemplateDto
        {
            Id = id,
            TemplateName = "TestTemplate",
            Module = "Notification",
            Category = "Email"
        };

        _templateStoreServiceMock.Setup(s => s.GetTemplateByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TemplateDto>.Success(dto));

        // Act
        var result = await _controller.GetById(id);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal(id, result.Data.Id);
    }

    [Fact]
    public async Task GetById_WithNonExistentId_ReturnsNotFound()
    {
        // Arrange
        var id = Guid.NewGuid();
        _templateStoreServiceMock.Setup(s => s.GetTemplateByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TemplateDto>.Failure("Template not found", 404));

        // Act
        var result = await _controller.GetById(id);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.Succeeded);
    }

    #endregion

    #region Create Tests

    [Fact]
    public async Task Create_WithValidRequest_CreatesTemplate()
    {
        // Arrange
        var request = new CreateTemplateRequest
        {
            TemplateName = "NewTemplate",
            Module = "Notification",
            Category = "Email",
            SubjectTemplate = "Subject",
            ContentTemplate = "Content"
        };

        var createdDto = new TemplateDto
        {
            Id = Guid.NewGuid(),
            TemplateName = request.TemplateName,
            Module = request.Module,
            Category = request.Category,
            SubjectTemplate = request.SubjectTemplate,
            ContentTemplate = request.ContentTemplate
        };

        _templateStoreServiceMock.Setup(s => s.CreateTemplateAsync(It.IsAny<CreateTemplateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TemplateDto>.Success(createdDto));

        // Act
        var result = await _controller.Create(request);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal(request.TemplateName, result.Data.TemplateName);
        _templateStoreServiceMock.Verify(s => s.CreateTemplateAsync(It.IsAny<CreateTemplateRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Update Tests

    [Fact]
    public async Task Update_WithValidRequest_UpdatesTemplate()
    {
        // Arrange
        var id = Guid.NewGuid();
        var request = new UpdateTemplateRequest
        {
            TemplateName = "UpdatedTemplate",
            Module = "Notification",
            Category = "Email",
            SubjectTemplate = "UpdatedSubject",
            ContentTemplate = "UpdatedContent"
        };

        var updatedDto = new TemplateDto
        {
            Id = id,
            TemplateName = request.TemplateName,
            Module = request.Module,
            Category = request.Category
        };

        _templateStoreServiceMock.Setup(s => s.UpdateTemplateAsync(id, It.IsAny<UpdateTemplateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TemplateDto>.Success(updatedDto));

        // Act
        var result = await _controller.Update(id, request);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        _templateStoreServiceMock.Verify(s => s.UpdateTemplateAsync(id, It.IsAny<UpdateTemplateRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Delete Tests

    [Fact]
    public async Task Delete_WithValidId_DeletesTemplate()
    {
        // Arrange
        var id = Guid.NewGuid();
        _templateStoreServiceMock.Setup(s => s.DeleteTemplateAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        // Act
        var result = await _controller.Delete(id);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        _templateStoreServiceMock.Verify(s => s.DeleteTemplateAsync(id, It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region DeleteBatch Tests

    [Fact]
    public async Task DeleteBatch_WithValidIds_DeletesTemplates()
    {
        // Arrange
        var ids = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        _templateStoreServiceMock.Setup(s => s.DeleteTemplatesAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success("Successfully deleted 2 templates"));

        // Act
        var result = await _controller.DeleteBatch(ids);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        _templateStoreServiceMock.Verify(s => s.DeleteTemplatesAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region GetAllTemplates Tests

    [Fact]
    public async Task GetAllTemplates_WithoutFilters_ReturnsAllTemplates()
    {
        // Arrange
        var dtos = new List<TemplateInfoDto>
        {
            new TemplateInfoDto { Id = Guid.NewGuid(), TemplateName = "Template1", Module = "Notification", Category = "Email" },
            new TemplateInfoDto { Id = Guid.NewGuid(), TemplateName = "Template2", Module = "Invoice", Category = "PDF" }
        };
        var pagedList = new PagedList<TemplateInfoDto>(dtos, 1, 20, 2);

        _templateStoreServiceMock.Setup(s => s.QueryTemplatesAsync(It.IsAny<QueryTemplateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IPagedList<TemplateInfoDto>>.Success(pagedList));

        var request = new QueryTemplateRequest
        {
            PageIndex = 1,
            PageSize = 20
        };

        // Act
        var result = await _controller.GetAllTemplates(request);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(2, result.Data.TotalCount);
    }

    [Fact]
    public async Task GetAllTemplates_WithModuleFilter_ReturnsFilteredTemplates()
    {
        // Arrange
        var dtos = new List<TemplateInfoDto>
        {
            new TemplateInfoDto { Id = Guid.NewGuid(), TemplateName = "Template1", Module = "Notification", Category = "Email" }
        };
        var pagedList = new PagedList<TemplateInfoDto>(dtos, 1, 20, 1);

        _templateStoreServiceMock.Setup(s => s.QueryTemplatesAsync(It.IsAny<QueryTemplateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IPagedList<TemplateInfoDto>>.Success(pagedList));

        var request = new QueryTemplateRequest
        {
            Module = "Notification",
            PageIndex = 1,
            PageSize = 20
        };

        // Act
        var result = await _controller.GetAllTemplates(request);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal(1, result.Data!.TotalCount);
    }

    #endregion
}

/// <summary>
/// 测试用的具体控制器实现
/// </summary>
public class TestTemplateController : TemplateAdminControllerBase
{
    public TestTemplateController(
        ITemplateStoreService templateStoreService,
        ITemplateEngine? templateEngine = null)
        : base(templateStoreService, templateEngine)
    {
    }
}
