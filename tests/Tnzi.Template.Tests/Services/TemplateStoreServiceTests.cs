
namespace Tnzi.Template.Tests.Services;

/// <summary>
/// 模板存储服务单元测试
/// </summary>
public class TemplateStoreServiceTests
{
    private readonly Mock<IRepository<Entities.Template, Guid>> _repositoryMock;
    private readonly Mock<IServiceProvider> _serviceProviderMock;
    private readonly TemplateStoreService _service;

    public TemplateStoreServiceTests()
    {
        // 初始化 Mapster（MapTo 扩展方法需要）
        var config = new TypeAdapterConfig();
        MapperExtensions.SetMapper(new Mapper(config));

        _repositoryMock = new Mock<IRepository<Entities.Template, Guid>>();
        _serviceProviderMock = new Mock<IServiceProvider>();

        // 设置 ILoggerFactory（ApplicationService.Logger 需要）
        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>()))
            .Returns(new Mock<ILogger>().Object);
        _serviceProviderMock.Setup(sp => sp.GetService(typeof(ILoggerFactory)))
            .Returns(loggerFactory.Object);

        _service = new TemplateStoreService(_repositoryMock.Object, _serviceProviderMock.Object);
    }

    /// <summary>
    /// 设置仓储的 IQueryable 成员，使 LINQ 扩展方法能正常工作
    /// </summary>
    private void SetupQueryable(List<Entities.Template> templates)
    {
        var mockQueryable = templates.BuildMock();
        _repositoryMock.As<IQueryable<Entities.Template>>()
            .Setup(q => q.Provider).Returns(mockQueryable.Provider);
        _repositoryMock.As<IQueryable<Entities.Template>>()
            .Setup(q => q.Expression).Returns(mockQueryable.Expression);
        _repositoryMock.As<IQueryable<Entities.Template>>()
            .Setup(q => q.ElementType).Returns(mockQueryable.ElementType);
        _repositoryMock.As<IQueryable<Entities.Template>>()
            .Setup(q => q.GetEnumerator()).Returns(() => mockQueryable.GetEnumerator());
    }

    #region GetTemplateAsync Tests

    [Fact]
    public async Task GetTemplateAsync_WithValidParameters_ReturnsTemplate()
    {
        // Arrange
        var template = new Entities.Template
        {
            Id = Guid.NewGuid(),
            TemplateName = "TestTemplate",
            Module = "Notification",
            Category = "Email",
            SubjectTemplate = "Subject",
            ContentTemplate = "Content",
            IsActive = true
        };

        SetupQueryable(new List<Entities.Template> { template });

        // Act
        var result = await _service.GetTemplateAsync("TestTemplate", "Notification", "Email");

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal("TestTemplate", result.Data.TemplateName);
        Assert.Equal("Notification", result.Data.Module);
        Assert.Equal("Email", result.Data.Category);
    }

    [Fact]
    public async Task GetTemplateAsync_WithEmptyTemplateName_ReturnsNull()
    {
        // Act
        var result = await _service.GetTemplateAsync("", "Notification");

        // Assert
        Assert.False(result.Succeeded);
        Assert.Null(result.Data);
    }

    [Fact]
    public async Task GetTemplateAsync_WithEmptyModule_ReturnsFailResult()
    {
        // Act
        var result = await _service.GetTemplateAsync("TestTemplate", "");

        // Assert
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task GetTemplateAsync_WithNullCategory_ReturnsTemplate()
    {
        // Arrange
        var template = new Entities.Template
        {
            Id = Guid.NewGuid(),
            TemplateName = "TestTemplate",
            Module = "Notification",
            Category = "Email",
            IsActive = true
        };

        SetupQueryable(new List<Entities.Template> { template });

        // Act
        var result = await _service.GetTemplateAsync("TestTemplate", "Notification", null);

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
    }

    #endregion

    #region GetTemplateByIdAsync Tests

    [Fact]
    public async Task GetTemplateByIdAsync_WithValidId_ReturnsTemplateDto()
    {
        // Arrange
        var id = Guid.NewGuid();
        var template = new Entities.Template { Id = id, TemplateName = "Test", Module = "Notification", Category = "Email" };

        _repositoryMock.Setup(r => r.GetAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        // Act
        var result = await _service.GetTemplateByIdAsync(id);

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal(id, result.Data.Id);
    }

    [Fact]
    public async Task GetTemplateByIdAsync_WithNonExistentId_ReturnsNull()
    {
        // Arrange
        var id = Guid.NewGuid();
        _repositoryMock.Setup(r => r.GetAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Entities.Template?)null);

        // Act
        var result = await _service.GetTemplateByIdAsync(id);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Null(result.Data);
    }

    #endregion

    #region CreateTemplateAsync Tests

    [Fact]
    public async Task CreateTemplateAsync_WithValidRequest_CreatesTemplate()
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

        _repositoryMock.Setup(r => r.AnyAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Entities.Template, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _repositoryMock.Setup(r => r.InsertAsync(It.IsAny<Entities.Template>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.CreateTemplateAsync(request);

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal("NewTemplate", result.Data.TemplateName);
        _repositoryMock.Verify(r => r.InsertAsync(It.IsAny<Entities.Template>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateTemplateAsync_WithDuplicateName_ReturnsFailResult()
    {
        // Arrange
        var request = new CreateTemplateRequest
        {
            TemplateName = "DuplicateTemplate",
            Module = "Notification",
            Category = "Email",
            ContentTemplate = "Content"
        };

        _repositoryMock.Setup(r => r.AnyAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Entities.Template, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _service.CreateTemplateAsync(request);

        // Assert
        Assert.False(result.Succeeded);
    }

    #endregion

    #region UpdateTemplateAsync Tests

    [Fact]
    public async Task UpdateTemplateAsync_WithValidRequest_UpdatesTemplate()
    {
        // Arrange
        var id = Guid.NewGuid();
        var existing = new Entities.Template
        {
            Id = id,
            TemplateName = "OldName",
            Module = "Notification",
            Category = "Email"
        };

        var request = new UpdateTemplateRequest
        {
            TemplateName = "NewName",
            Module = "Notification",
            Category = "Email",
            SubjectTemplate = "NewSubject",
            ContentTemplate = "NewContent"
        };

        _repositoryMock.Setup(r => r.GetAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _repositoryMock.Setup(r => r.AnyAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Entities.Template, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _repositoryMock.Setup(r => r.UpdateAsync(It.IsAny<Entities.Template>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.UpdateTemplateAsync(id, request);

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        _repositoryMock.Verify(r => r.UpdateAsync(It.IsAny<Entities.Template>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateTemplateAsync_WithNonExistentId_ReturnsFailResult()
    {
        // Arrange
        var id = Guid.NewGuid();
        var request = new UpdateTemplateRequest
        {
            TemplateName = "Test",
            Module = "Notification",
            Category = "Email",
            ContentTemplate = "Content"
        };

        _repositoryMock.Setup(r => r.GetAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Entities.Template?)null);

        // Act
        var result = await _service.UpdateTemplateAsync(id, request);

        // Assert
        Assert.False(result.Succeeded);
    }

    #endregion

    #region DeleteTemplateAsync Tests

    [Fact]
    public async Task DeleteTemplateAsync_WithValidId_DeletesTemplate()
    {
        // Arrange
        var id = Guid.NewGuid();
        var template = new Entities.Template
        {
            Id = id,
            TemplateName = "TestTemplate",
            Module = "Notification",
            Category = "Email"
        };

        _repositoryMock.Setup(r => r.GetAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        _repositoryMock.Setup(r => r.DeleteAsync(id, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.DeleteTemplateAsync(id);

        // Assert
        Assert.True(result.Succeeded);
        _repositoryMock.Verify(r => r.DeleteAsync(id, It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region DeleteTemplatesAsync Tests

    [Fact]
    public async Task DeleteTemplatesAsync_WithValidIds_DeletesTemplates()
    {
        // Arrange
        var ids = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        var templates = ids.Select(id => new Entities.Template { Id = id, TemplateName = $"Template_{id}" }).ToList();

        SetupQueryable(templates);
        _repositoryMock.Setup(r => r.DeleteManyAsync(It.IsAny<IEnumerable<Entities.Template>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.DeleteTemplatesAsync(ids);

        // Assert
        Assert.True(result.Succeeded);
        _repositoryMock.Verify(r => r.DeleteManyAsync(It.IsAny<IEnumerable<Entities.Template>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteTemplatesAsync_WithNoMatchingIds_ReturnsFailResult()
    {
        // Arrange
        var ids = new List<Guid> { Guid.NewGuid() };
        SetupQueryable(new List<Entities.Template>());

        // Act
        var result = await _service.DeleteTemplatesAsync(ids);

        // Assert
        Assert.False(result.Succeeded);
    }

    #endregion

    #region GetAllTemplatesAsync Tests

    [Fact]
    public async Task GetAllTemplatesAsync_WithoutFilters_ReturnsAllTemplates()
    {
        // Arrange
        var templates = new List<Entities.Template>
        {
            new Entities.Template { Id = Guid.NewGuid(), TemplateName = "Template1", Module = "Notification", Category = "Email" },
            new Entities.Template { Id = Guid.NewGuid(), TemplateName = "Template2", Module = "Invoice", Category = "PDF" }
        };

        SetupQueryable(templates);

        // Act
        var result = await _service.GetAllTemplatesAsync();

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal(2, result.Data.Count());
    }

    [Fact]
    public async Task GetAllTemplatesAsync_WithModuleFilter_ReturnsFilteredTemplates()
    {
        // Arrange
        var templates = new List<Entities.Template>
        {
            new Entities.Template { Id = Guid.NewGuid(), TemplateName = "Template1", Module = "Notification", Category = "Email" },
            new Entities.Template { Id = Guid.NewGuid(), TemplateName = "Template2", Module = "Invoice", Category = "PDF" }
        };

        SetupQueryable(templates);

        // Act
        var result = await _service.GetAllTemplatesAsync("Notification");

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
    }

    [Fact]
    public async Task GetAllTemplatesAsync_WithModuleAndCategoryFilter_ReturnsFilteredTemplates()
    {
        // Arrange
        var templates = new List<Entities.Template>
        {
            new Entities.Template { Id = Guid.NewGuid(), TemplateName = "Template1", Module = "Notification", Category = "Email" }
        };

        SetupQueryable(templates);

        // Act
        var result = await _service.GetAllTemplatesAsync("Notification", "Email");

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
    }

    #endregion

    #region ExportTemplatesAsync Tests

    /// <summary>
    /// 设置仓储的 AsQueryable mock（用于 ExportTemplatesAsync 等使用 _repository.AsQueryable() 的方法）
    /// </summary>
    private void SetupAsQueryable(List<Entities.Template> templates)
    {
        var mockQueryable = templates.BuildMock();
        _repositoryMock.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(mockQueryable);
        // Also set the IQueryable interface for compatibility
        _repositoryMock.As<IQueryable<Entities.Template>>()
            .Setup(q => q.Provider).Returns(mockQueryable.Provider);
        _repositoryMock.As<IQueryable<Entities.Template>>()
            .Setup(q => q.Expression).Returns(mockQueryable.Expression);
        _repositoryMock.As<IQueryable<Entities.Template>>()
            .Setup(q => q.ElementType).Returns(mockQueryable.ElementType);
        _repositoryMock.As<IQueryable<Entities.Template>>()
            .Setup(q => q.GetEnumerator()).Returns(() => mockQueryable.GetEnumerator());
    }

    [Fact]
    public async Task ExportTemplatesAsync_WithNoFilter_ExportsAllTemplates()
    {
        // Arrange
        var templates = new List<Entities.Template>
        {
            new() { Id = Guid.NewGuid(), TemplateName = "WelcomeEmail", Module = "Notification", Category = "Email", SubjectTemplate = "Welcome!", ContentTemplate = "<p>Welcome</p>", IsActive = true, IsDeleted = false },
            new() { Id = Guid.NewGuid(), TemplateName = "OrderConfirm", Module = "Payment", Category = "Email", SubjectTemplate = "Order", ContentTemplate = "<p>Order</p>", IsActive = true, IsDeleted = false }
        };

        SetupAsQueryable(templates);

        // Act
        var result = await _service.ExportTemplatesAsync();

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Contains("WelcomeEmail", result.Data);
        Assert.Contains("OrderConfirm", result.Data);
    }

    [Fact]
    public async Task ExportTemplatesAsync_WithModuleFilter_ExportsFilteredTemplates()
    {
        // Arrange
        var templates = new List<Entities.Template>
        {
            new() { Id = Guid.NewGuid(), TemplateName = "WelcomeEmail", Module = "Notification", Category = "Email", SubjectTemplate = "Welcome!", ContentTemplate = "<p>Welcome</p>", IsActive = true, IsDeleted = false },
            new() { Id = Guid.NewGuid(), TemplateName = "InvoiceTemplate", Module = "Payment", Category = "PDF", SubjectTemplate = "Invoice", ContentTemplate = "<p>Invoice</p>", IsActive = true, IsDeleted = false }
        };

        SetupAsQueryable(templates);

        // Act
        var result = await _service.ExportTemplatesAsync(module: "Notification");

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Contains("WelcomeEmail", result.Data);
        Assert.DoesNotContain("InvoiceTemplate", result.Data);
    }

    [Fact]
    public async Task ExportTemplatesAsync_ExcludesDeletedTemplates()
    {
        // Arrange
        var templates = new List<Entities.Template>
        {
            new() { Id = Guid.NewGuid(), TemplateName = "ActiveTemplate", Module = "Notification", IsActive = true, IsDeleted = false },
            new() { Id = Guid.NewGuid(), TemplateName = "DeletedTemplate", Module = "Notification", IsActive = true, IsDeleted = true }
        };

        SetupAsQueryable(templates);

        // Act
        var result = await _service.ExportTemplatesAsync();

        // Assert
        Assert.True(result.Succeeded);
        Assert.Contains("ActiveTemplate", result.Data!);
        Assert.DoesNotContain("DeletedTemplate", result.Data!);
    }

    #endregion

    #region ImportTemplatesAsync Tests

    [Fact]
    public async Task ImportTemplatesAsync_WithValidJson_CreatesNewTemplates()
    {
        // Arrange
        var json = """
        [
            {
                "templateName": "NewTemplate",
                "module": "Notification",
                "category": "Email",
                "subjectTemplate": "Subject",
                "contentTemplate": "<p>Content</p>",
                "isActive": true
            }
        ]
        """;

        // No existing templates
        SetupQueryable(new List<Entities.Template>());
        _repositoryMock.Setup(r => r.FirstOrDefaultAsync(
            It.IsAny<Expression<Func<Entities.Template, bool>>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((Entities.Template?)null);

        // Act
        var result = await _service.ImportTemplatesAsync(json);

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal(1, result.Data.CreatedCount);
        Assert.Equal(0, result.Data.UpdatedCount);
        Assert.Equal(0, result.Data.SkippedCount);
    }

    [Fact]
    public async Task ImportTemplatesAsync_WithInvalidJson_ReturnsFail()
    {
        // Act
        var result = await _service.ImportTemplatesAsync("invalid json {{{");

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
    }

    [Fact]
    public async Task ImportTemplatesAsync_WithEmptyArray_ReturnsFail()
    {
        // Act
        var result = await _service.ImportTemplatesAsync("[]");

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
    }

    [Fact]
    public async Task ImportTemplatesAsync_WithMissingTemplateName_SkipsEntry()
    {
        // Arrange
        var json = """
        [
            {
                "templateName": "",
                "module": "Notification"
            }
        ]
        """;

        SetupQueryable(new List<Entities.Template>());

        // Act
        var result = await _service.ImportTemplatesAsync(json);

        // Assert
        Assert.True(result.Succeeded);
        Assert.Equal(0, result.Data!.CreatedCount);
        Assert.Equal(1, result.Data.SkippedCount);
        Assert.Single(result.Data.Errors);
    }

    [Fact]
    public async Task ImportTemplatesAsync_ExistingTemplate_OverwriteFalse_SkipsEntry()
    {
        // Arrange
        var json = """
        [
            {
                "templateName": "ExistingTemplate",
                "module": "Notification",
                "contentTemplate": "new content"
            }
        ]
        """;

        var existingTemplate = new Entities.Template
        {
            Id = Guid.NewGuid(),
            TemplateName = "ExistingTemplate",
            Module = "Notification",
            ContentTemplate = "old content"
        };

        SetupQueryable(new List<Entities.Template> { existingTemplate });
        _repositoryMock.Setup(r => r.FirstOrDefaultAsync(
            It.IsAny<Expression<Func<Entities.Template, bool>>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingTemplate);

        // Act
        var result = await _service.ImportTemplatesAsync(json, overwriteExisting: false);

        // Assert
        Assert.True(result.Succeeded);
        Assert.Equal(0, result.Data!.CreatedCount);
        Assert.Equal(0, result.Data.UpdatedCount);
        Assert.Equal(1, result.Data.SkippedCount);
    }

    [Fact]
    public async Task ImportTemplatesAsync_ExistingTemplate_OverwriteTrue_UpdatesEntry()
    {
        // Arrange
        var json = """
        [
            {
                "templateName": "ExistingTemplate",
                "module": "Notification",
                "contentTemplate": "updated content"
            }
        ]
        """;

        var existingTemplate = new Entities.Template
        {
            Id = Guid.NewGuid(),
            TemplateName = "ExistingTemplate",
            Module = "Notification",
            ContentTemplate = "old content"
        };

        SetupQueryable(new List<Entities.Template> { existingTemplate });
        _repositoryMock.Setup(r => r.FirstOrDefaultAsync(
            It.IsAny<Expression<Func<Entities.Template, bool>>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingTemplate);

        // Act
        var result = await _service.ImportTemplatesAsync(json, overwriteExisting: true);

        // Assert
        Assert.True(result.Succeeded);
        Assert.Equal(0, result.Data!.CreatedCount);
        Assert.Equal(1, result.Data.UpdatedCount);
        Assert.Equal(0, result.Data.SkippedCount);
    }

    #endregion
}
