using TemplateEntity = Tnzi.Template.Entities.Template;

namespace Tnzi.Template.Tests;

/// <summary>
/// Template 模块 E1 增强功能深度单元测试
/// 覆盖：Layout Export/Import、Template Variable Discovery、Batch Activate/Deactivate
/// </summary>
public class TemplateDeepIterationTests
{
    #region LayoutStoreService Tests

    /// <summary>
    /// LayoutStoreService E1 增强测试
    /// </summary>
    public class LayoutStoreServiceE1Tests
    {
        private readonly Mock<IRepository<Layout, Guid>> _repositoryMock;
        private readonly Mock<IServiceProvider> _serviceProviderMock;
        private readonly LayoutStoreService _service;

        public LayoutStoreServiceE1Tests()
        {
            _repositoryMock = new Mock<IRepository<Layout, Guid>>();
            _serviceProviderMock = new Mock<IServiceProvider>();

            // 设置 ILoggerFactory（ApplicationService.Logger 需要）
            var loggerFactory = new Mock<ILoggerFactory>();
            loggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>()))
                .Returns(new Mock<ILogger>().Object);
            _serviceProviderMock.Setup(sp => sp.GetService(typeof(ILoggerFactory)))
                .Returns(loggerFactory.Object);

            _service = new LayoutStoreService(_repositoryMock.Object, _serviceProviderMock.Object);
        }

        /// <summary>
        /// 设置仓储的 IQueryable 成员（用于 Where 链式调用）
        /// </summary>
        private void SetupQueryable(List<Layout> layouts)
        {
            var mockQueryable = layouts.BuildMock();
            _repositoryMock.As<IQueryable<Layout>>()
                .Setup(q => q.Provider).Returns(mockQueryable.Provider);
            _repositoryMock.As<IQueryable<Layout>>()
                .Setup(q => q.Expression).Returns(mockQueryable.Expression);
            _repositoryMock.As<IQueryable<Layout>>()
                .Setup(q => q.ElementType).Returns(mockQueryable.ElementType);
            _repositoryMock.As<IQueryable<Layout>>()
                .Setup(q => q.GetEnumerator()).Returns(() => mockQueryable.GetEnumerator());
        }

        /// <summary>
        /// 设置仓储的 AsQueryable mock（用于 ExportLayoutsAsync 等使用 _repository.AsQueryable() 的方法）
        /// </summary>
        private void SetupAsQueryable(List<Layout> layouts)
        {
            var mockQueryable = layouts.BuildMock();
            _repositoryMock.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(mockQueryable);
            _repositoryMock.As<IQueryable<Layout>>()
                .Setup(q => q.Provider).Returns(mockQueryable.Provider);
            _repositoryMock.As<IQueryable<Layout>>()
                .Setup(q => q.Expression).Returns(mockQueryable.Expression);
            _repositoryMock.As<IQueryable<Layout>>()
                .Setup(q => q.ElementType).Returns(mockQueryable.ElementType);
            _repositoryMock.As<IQueryable<Layout>>()
                .Setup(q => q.GetEnumerator()).Returns(() => mockQueryable.GetEnumerator());
        }

        #region E1-1: ExportLayoutsAsync Tests

        [Fact]
        public async Task ExportLayoutsAsync_WithNoFilter_ExportsAllLayouts()
        {
            // Arrange
            var layouts = new List<Layout>
            {
                new() { Id = Guid.NewGuid(), LayoutName = "DefaultEmail", Module = "Notification", Category = "Email", LayoutContent = "<html>@Model[\"Content\"]</html>", IsActive = true, IsDefault = true, IsDeleted = false },
                new() { Id = Guid.NewGuid(), LayoutName = "DefaultSms", Module = "Notification", Category = "Sms", LayoutContent = "@Model[\"Content\"]", IsActive = true, IsDefault = false, IsDeleted = false }
            };

            SetupAsQueryable(layouts);

            // Act
            var result = await _service.ExportLayoutsAsync();

            // Assert
            Assert.True(result.Succeeded);
            Assert.NotNull(result.Data);
            Assert.Contains("DefaultEmail", result.Data);
            Assert.Contains("DefaultSms", result.Data);
        }

        [Fact]
        public async Task ExportLayoutsAsync_WithModuleFilter_ExportsFilteredLayouts()
        {
            // Arrange
            var layouts = new List<Layout>
            {
                new() { Id = Guid.NewGuid(), LayoutName = "EmailLayout", Module = "Notification", Category = "Email", LayoutContent = "<html></html>", IsActive = true, IsDeleted = false },
                new() { Id = Guid.NewGuid(), LayoutName = "InvoiceLayout", Module = "Payment", Category = "PDF", LayoutContent = "<div></div>", IsActive = true, IsDeleted = false }
            };

            SetupAsQueryable(layouts);

            // Act
            var result = await _service.ExportLayoutsAsync(module: "Notification");

            // Assert
            Assert.True(result.Succeeded);
            Assert.NotNull(result.Data);
            Assert.Contains("EmailLayout", result.Data);
            Assert.DoesNotContain("InvoiceLayout", result.Data);
        }

        [Fact]
        public async Task ExportLayoutsAsync_ExcludesDeletedLayouts()
        {
            // Arrange
            var layouts = new List<Layout>
            {
                new() { Id = Guid.NewGuid(), LayoutName = "ActiveLayout", Module = "Notification", IsActive = true, IsDeleted = false },
                new() { Id = Guid.NewGuid(), LayoutName = "DeletedLayout", Module = "Notification", IsActive = true, IsDeleted = true }
            };

            SetupAsQueryable(layouts);

            // Act
            var result = await _service.ExportLayoutsAsync();

            // Assert
            Assert.True(result.Succeeded);
            Assert.Contains("ActiveLayout", result.Data!);
            Assert.DoesNotContain("DeletedLayout", result.Data!);
        }

        [Fact]
        public async Task ExportLayoutsAsync_ProducesValidJson()
        {
            // Arrange
            var layouts = new List<Layout>
            {
                new() { Id = Guid.NewGuid(), LayoutName = "TestLayout", Module = "Notification", Category = "Email", LayoutContent = "<html>body</html>", IsActive = true, IsDefault = true, Description = "Test description", IsDeleted = false }
            };

            SetupAsQueryable(layouts);

            // Act
            var result = await _service.ExportLayoutsAsync();

            // Assert
            Assert.True(result.Succeeded);
            // 验证导出的 JSON 可以成功反序列化
            var entries = System.Text.Json.JsonSerializer.Deserialize<List<LayoutExportEntry>>(result.Data!, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true
            });
            Assert.NotNull(entries);
            Assert.Single(entries);
            Assert.Equal("TestLayout", entries[0].LayoutName);
            Assert.Equal("Notification", entries[0].Module);
            Assert.Equal("Email", entries[0].Category);
            Assert.True(entries[0].IsDefault);
        }

        #endregion

        #region E1-1: ImportLayoutsAsync Tests

        [Fact]
        public async Task ImportLayoutsAsync_WithValidJson_CreatesNewLayouts()
        {
            // Arrange
            var json = """
            [
                {
                    "layoutName": "NewEmailLayout",
                    "module": "Notification",
                    "category": "Email",
                    "layoutContent": "<html>@Model[\"Content\"]</html>",
                    "isActive": true,
                    "isDefault": false
                }
            ]
            """;

            SetupQueryable(new List<Layout>());
            _repositoryMock.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<Layout, bool>>>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync((Layout?)null);

            // Act
            var result = await _service.ImportLayoutsAsync(json);

            // Assert
            Assert.True(result.Succeeded);
            Assert.NotNull(result.Data);
            Assert.Equal(1, result.Data.CreatedCount);
            Assert.Equal(0, result.Data.UpdatedCount);
            Assert.Equal(0, result.Data.SkippedCount);
            _repositoryMock.Verify(r => r.InsertManyAsync(
                It.Is<IEnumerable<Layout>>(list => list.Count() == 1),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task ImportLayoutsAsync_ExistingLayout_OverwriteFalse_SkipsEntry()
        {
            // Arrange
            var json = """
            [
                {
                    "layoutName": "ExistingLayout",
                    "module": "Notification",
                    "category": "Email",
                    "layoutContent": "new content"
                }
            ]
            """;

            var existingLayout = new Layout
            {
                Id = Guid.NewGuid(),
                LayoutName = "ExistingLayout",
                Module = "Notification",
                Category = "Email",
                LayoutContent = "old content"
            };

            SetupQueryable(new List<Layout> { existingLayout });
            _repositoryMock.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<Layout, bool>>>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(existingLayout);

            // Act
            var result = await _service.ImportLayoutsAsync(json, overwriteExisting: false);

            // Assert
            Assert.True(result.Succeeded);
            Assert.Equal(0, result.Data!.CreatedCount);
            Assert.Equal(0, result.Data.UpdatedCount);
            Assert.Equal(1, result.Data.SkippedCount);
        }

        [Fact]
        public async Task ImportLayoutsAsync_ExistingLayout_OverwriteTrue_UpdatesEntry()
        {
            // Arrange
            var json = """
            [
                {
                    "layoutName": "ExistingLayout",
                    "module": "Notification",
                    "category": "Email",
                    "layoutContent": "updated content",
                    "isActive": true,
                    "isDefault": true,
                    "description": "Updated description"
                }
            ]
            """;

            var existingLayout = new Layout
            {
                Id = Guid.NewGuid(),
                LayoutName = "ExistingLayout",
                Module = "Notification",
                Category = "Email",
                LayoutContent = "old content"
            };

            SetupQueryable(new List<Layout> { existingLayout });
            _repositoryMock.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<Layout, bool>>>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(existingLayout);

            // Act
            var result = await _service.ImportLayoutsAsync(json, overwriteExisting: true);

            // Assert
            Assert.True(result.Succeeded);
            Assert.Equal(0, result.Data!.CreatedCount);
            Assert.Equal(1, result.Data.UpdatedCount);
            Assert.Equal(0, result.Data.SkippedCount);
            _repositoryMock.Verify(r => r.UpdateAsync(
                It.Is<Layout>(l => l.LayoutContent == "updated content"),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task ImportLayoutsAsync_WithInvalidJson_ReturnsFail()
        {
            // Act
            var result = await _service.ImportLayoutsAsync("invalid json {{{");

            // Assert
            Assert.False(result.Succeeded);
            Assert.Equal(400, result.Code);
        }

        [Fact]
        public async Task ImportLayoutsAsync_WithEmptyLayoutName_SkipsEntry()
        {
            // Arrange
            var json = """
            [
                {
                    "layoutName": "",
                    "module": "Notification",
                    "layoutContent": "<html></html>"
                }
            ]
            """;

            SetupQueryable(new List<Layout>());

            // Act
            var result = await _service.ImportLayoutsAsync(json);

            // Assert
            Assert.True(result.Succeeded);
            Assert.Equal(0, result.Data!.CreatedCount);
            Assert.Equal(1, result.Data.SkippedCount);
            Assert.Single(result.Data.Errors);
        }

        [Fact]
        public async Task ImportLayoutsAsync_WithEmptyArray_ReturnsFail()
        {
            // Act
            var result = await _service.ImportLayoutsAsync("[]");

            // Assert
            Assert.False(result.Succeeded);
            Assert.Equal(400, result.Code);
        }

        [Fact]
        public async Task ImportLayoutsAsync_MultipleEntries_MixedResults()
        {
            // Arrange - 两个布局：一个新建，一个已存在（跳过）
            var json = """
            [
                {
                    "layoutName": "NewLayout",
                    "module": "Notification",
                    "category": "Email",
                    "layoutContent": "<div>new</div>",
                    "isActive": true
                },
                {
                    "layoutName": "ExistingLayout",
                    "module": "Notification",
                    "category": "Email",
                    "layoutContent": "<div>existing</div>",
                    "isActive": true
                }
            ]
            """;

            var existingLayout = new Layout
            {
                Id = Guid.NewGuid(),
                LayoutName = "ExistingLayout",
                Module = "Notification",
                Category = "Email",
                LayoutContent = "old content"
            };

            SetupQueryable(new List<Layout> { existingLayout });

            // FirstOrDefaultAsync 第一次调用返回 null（新布局），第二次返回已存在的布局
            _repositoryMock.SetupSequence(r => r.FirstOrDefaultAsync(
                It.IsAny<Expression<Func<Layout, bool>>>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync((Layout?)null)
                .ReturnsAsync(existingLayout);

            // Act
            var result = await _service.ImportLayoutsAsync(json, overwriteExisting: false);

            // Assert
            Assert.True(result.Succeeded);
            Assert.Equal(1, result.Data!.CreatedCount);
            Assert.Equal(0, result.Data.UpdatedCount);
            Assert.Equal(1, result.Data.SkippedCount);
        }

        #endregion

        #region E1-3: Layout BatchActivateAsync Tests

        [Fact]
        public async Task BatchActivateAsync_ActivatesMultipleLayouts()
        {
            // Arrange
            var id1 = Guid.NewGuid();
            var id2 = Guid.NewGuid();
            var layouts = new List<Layout>
            {
                new() { Id = id1, LayoutName = "Layout1", Module = "Notification", IsActive = false, IsDeleted = false },
                new() { Id = id2, LayoutName = "Layout2", Module = "Notification", IsActive = false, IsDeleted = false }
            };

            SetupQueryable(layouts);

            // Act
            var result = await _service.BatchActivateAsync(new List<Guid> { id1, id2 }, isActive: true);

            // Assert
            Assert.True(result.Succeeded);
            Assert.Equal(2, result.Data);
            _repositoryMock.Verify(r => r.UpdateManyAsync(
                It.IsAny<IEnumerable<Layout>>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task BatchActivateAsync_DeactivatesMultipleLayouts()
        {
            // Arrange
            var id1 = Guid.NewGuid();
            var id2 = Guid.NewGuid();
            var layouts = new List<Layout>
            {
                new() { Id = id1, LayoutName = "Layout1", Module = "Notification", IsActive = true, IsDeleted = false },
                new() { Id = id2, LayoutName = "Layout2", Module = "Notification", IsActive = true, IsDeleted = false }
            };

            SetupQueryable(layouts);

            // Act
            var result = await _service.BatchActivateAsync(new List<Guid> { id1, id2 }, isActive: false);

            // Assert
            Assert.True(result.Succeeded);
            Assert.Equal(2, result.Data);
            _repositoryMock.Verify(r => r.UpdateManyAsync(
                It.IsAny<IEnumerable<Layout>>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task BatchActivateAsync_AlreadyInTargetState_ReturnsZeroChanged()
        {
            // Arrange
            var id1 = Guid.NewGuid();
            var layouts = new List<Layout>
            {
                new() { Id = id1, LayoutName = "Layout1", Module = "Notification", IsActive = true, IsDeleted = false }
            };

            SetupQueryable(layouts);

            // Act - 尝试激活一个已经是激活状态的布局
            var result = await _service.BatchActivateAsync(new List<Guid> { id1 }, isActive: true);

            // Assert
            Assert.True(result.Succeeded);
            Assert.Equal(0, result.Data);
            // 无变更时不应调用 UpdateManyAsync
            _repositoryMock.Verify(r => r.UpdateManyAsync(
                It.IsAny<IEnumerable<Layout>>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task BatchActivateAsync_NonExistentIds_ReturnsFailResult()
        {
            // Arrange
            SetupQueryable(new List<Layout>());

            // Act
            var result = await _service.BatchActivateAsync(new List<Guid> { Guid.NewGuid() }, isActive: true);

            // Assert
            Assert.False(result.Succeeded);
            Assert.Equal(404, result.Code);
        }

        [Fact]
        public async Task BatchActivateAsync_ExcludesDeletedLayouts()
        {
            // Arrange
            var id1 = Guid.NewGuid();
            var id2 = Guid.NewGuid();
            var layouts = new List<Layout>
            {
                new() { Id = id1, LayoutName = "ActiveLayout", Module = "Notification", IsActive = false, IsDeleted = false },
                new() { Id = id2, LayoutName = "DeletedLayout", Module = "Notification", IsActive = false, IsDeleted = true }
            };

            SetupQueryable(layouts);

            // Act
            var result = await _service.BatchActivateAsync(new List<Guid> { id1, id2 }, isActive: true);

            // Assert
            Assert.True(result.Succeeded);
            // 只有非删除的布局会被修改
            Assert.Equal(1, result.Data);
        }

        [Fact]
        public async Task BatchActivateAsync_MixedStates_OnlyChangesNeeded()
        {
            // Arrange
            var id1 = Guid.NewGuid();
            var id2 = Guid.NewGuid();
            var layouts = new List<Layout>
            {
                new() { Id = id1, LayoutName = "InactiveLayout", Module = "Notification", IsActive = false, IsDeleted = false },
                new() { Id = id2, LayoutName = "AlreadyActiveLayout", Module = "Notification", IsActive = true, IsDeleted = false }
            };

            SetupQueryable(layouts);

            // Act
            var result = await _service.BatchActivateAsync(new List<Guid> { id1, id2 }, isActive: true);

            // Assert
            Assert.True(result.Succeeded);
            Assert.Equal(1, result.Data); // 只有一个需要变更
        }

        #endregion
    }

    #endregion

    #region TemplateStoreService Tests

    /// <summary>
    /// TemplateStoreService E1 增强测试
    /// </summary>
    public class TemplateStoreServiceE1Tests
    {
        private readonly Mock<IRepository<TemplateEntity, Guid>> _repositoryMock;
        private readonly Mock<IServiceProvider> _serviceProviderMock;
        private readonly TemplateStoreService _service;

        public TemplateStoreServiceE1Tests()
        {
            _repositoryMock = new Mock<IRepository<TemplateEntity, Guid>>();
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
        /// 设置仓储的 IQueryable 成员
        /// </summary>
        private void SetupQueryable(List<TemplateEntity> templates)
        {
            var mockQueryable = templates.BuildMock();
            _repositoryMock.As<IQueryable<TemplateEntity>>()
                .Setup(q => q.Provider).Returns(mockQueryable.Provider);
            _repositoryMock.As<IQueryable<TemplateEntity>>()
                .Setup(q => q.Expression).Returns(mockQueryable.Expression);
            _repositoryMock.As<IQueryable<TemplateEntity>>()
                .Setup(q => q.ElementType).Returns(mockQueryable.ElementType);
            _repositoryMock.As<IQueryable<TemplateEntity>>()
                .Setup(q => q.GetEnumerator()).Returns(() => mockQueryable.GetEnumerator());
        }

        #region E1-2: GetTemplateVariablesAsync Tests

        [Fact]
        public async Task GetTemplateVariablesAsync_ExtractsModelVariablesFromContent()
        {
            // Arrange
            var id = Guid.NewGuid();
            var template = new TemplateEntity
            {
                Id = id,
                TemplateName = "WelcomeEmail",
                Module = "Notification",
                Category = "Email",
                SubjectTemplate = "",
                ContentTemplate = "<p>Hello @Model.UserName, your order #@Model.OrderId is confirmed.</p>"
            };

            _repositoryMock.Setup(r => r.GetAsync(id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(template);

            // Act
            var result = await _service.GetTemplateVariablesAsync(id);

            // Assert
            Assert.True(result.Succeeded);
            Assert.NotNull(result.Data);
            Assert.Contains("UserName", result.Data.ContentVariables);
            Assert.Contains("OrderId", result.Data.ContentVariables);
            Assert.Equal(2, result.Data.ContentVariables.Count);
        }

        [Fact]
        public async Task GetTemplateVariablesAsync_ExtractsVariablesFromBothSubjectAndContent()
        {
            // Arrange
            var id = Guid.NewGuid();
            var template = new TemplateEntity
            {
                Id = id,
                TemplateName = "OrderNotification",
                Module = "Notification",
                Category = "Email",
                SubjectTemplate = "Order @Model.OrderNumber confirmed",
                ContentTemplate = "<p>Dear @Model.CustomerName, total: @Model.TotalAmount</p>"
            };

            _repositoryMock.Setup(r => r.GetAsync(id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(template);

            // Act
            var result = await _service.GetTemplateVariablesAsync(id);

            // Assert
            Assert.True(result.Succeeded);
            Assert.Single(result.Data!.SubjectVariables); // OrderNumber
            Assert.Contains("OrderNumber", result.Data.SubjectVariables);
            Assert.Equal(2, result.Data.ContentVariables.Count); // CustomerName, TotalAmount
            Assert.Contains("CustomerName", result.Data.ContentVariables);
            Assert.Contains("TotalAmount", result.Data.ContentVariables);
            // AllVariables 应该包含所有去重后的变量
            Assert.Equal(3, result.Data.AllVariables.Count);
        }

        [Fact]
        public async Task GetTemplateVariablesAsync_HandlesNestedVariables()
        {
            // Arrange
            var id = Guid.NewGuid();
            var template = new TemplateEntity
            {
                Id = id,
                TemplateName = "UserProfile",
                Module = "Identity",
                Category = "Email",
                SubjectTemplate = "",
                ContentTemplate = "<p>@Model.User.Name lives at @Model.User.Address.City</p>"
            };

            _repositoryMock.Setup(r => r.GetAsync(id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(template);

            // Act
            var result = await _service.GetTemplateVariablesAsync(id);

            // Assert
            Assert.True(result.Succeeded);
            Assert.Contains("User.Name", result.Data!.ContentVariables);
            Assert.Contains("User.Address.City", result.Data.ContentVariables);
        }

        [Fact]
        public async Task GetTemplateVariablesAsync_ReturnsEmptyForTemplateWithNoVariables()
        {
            // Arrange
            var id = Guid.NewGuid();
            var template = new TemplateEntity
            {
                Id = id,
                TemplateName = "StaticTemplate",
                Module = "Notification",
                Category = "Email",
                SubjectTemplate = "Static Subject",
                ContentTemplate = "<p>This is a static email with no variables.</p>"
            };

            _repositoryMock.Setup(r => r.GetAsync(id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(template);

            // Act
            var result = await _service.GetTemplateVariablesAsync(id);

            // Assert
            Assert.True(result.Succeeded);
            Assert.Empty(result.Data!.SubjectVariables);
            Assert.Empty(result.Data.ContentVariables);
            Assert.Empty(result.Data.AllVariables);
        }

        [Fact]
        public async Task GetTemplateVariablesAsync_NonExistentTemplate_ReturnsFailResult()
        {
            // Arrange
            var id = Guid.NewGuid();
            _repositoryMock.Setup(r => r.GetAsync(id, It.IsAny<CancellationToken>()))
                .ReturnsAsync((TemplateEntity?)null);

            // Act
            var result = await _service.GetTemplateVariablesAsync(id);

            // Assert
            Assert.False(result.Succeeded);
            Assert.Equal(404, result.Code);
        }

        [Fact]
        public async Task GetTemplateVariablesAsync_DeduplicatesVariablesAcrossSubjectAndContent()
        {
            // Arrange
            var id = Guid.NewGuid();
            var template = new TemplateEntity
            {
                Id = id,
                TemplateName = "DuplicateVarsTemplate",
                Module = "Notification",
                Category = "Email",
                SubjectTemplate = "Hello @Model.UserName",
                ContentTemplate = "<p>Dear @Model.UserName, welcome to @Model.AppName!</p>"
            };

            _repositoryMock.Setup(r => r.GetAsync(id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(template);

            // Act
            var result = await _service.GetTemplateVariablesAsync(id);

            // Assert
            Assert.True(result.Succeeded);
            Assert.Single(result.Data!.SubjectVariables); // UserName
            Assert.Equal(2, result.Data.ContentVariables.Count); // UserName, AppName
            // AllVariables 应该去重：UserName 只出现一次
            Assert.Equal(2, result.Data.AllVariables.Count); // UserName, AppName
            Assert.Contains("UserName", result.Data.AllVariables);
            Assert.Contains("AppName", result.Data.AllVariables);
        }

        [Fact]
        public async Task GetTemplateVariablesAsync_ReturnsCorrectTemplateInfo()
        {
            // Arrange
            var id = Guid.NewGuid();
            var template = new TemplateEntity
            {
                Id = id,
                TemplateName = "InfoCheckTemplate",
                Module = "System",
                Category = "Alert",
                SubjectTemplate = "@Model.Title",
                ContentTemplate = "@Model.Body"
            };

            _repositoryMock.Setup(r => r.GetAsync(id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(template);

            // Act
            var result = await _service.GetTemplateVariablesAsync(id);

            // Assert
            Assert.True(result.Succeeded);
            Assert.Equal(id, result.Data!.TemplateId);
            Assert.Equal("InfoCheckTemplate", result.Data.TemplateName);
        }

        [Fact]
        public async Task GetTemplateVariablesAsync_HandlesEmptySubjectTemplate()
        {
            // Arrange
            var id = Guid.NewGuid();
            var template = new TemplateEntity
            {
                Id = id,
                TemplateName = "NoSubjectTemplate",
                Module = "Notification",
                SubjectTemplate = "",
                ContentTemplate = "<p>@Model.Message</p>"
            };

            _repositoryMock.Setup(r => r.GetAsync(id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(template);

            // Act
            var result = await _service.GetTemplateVariablesAsync(id);

            // Assert
            Assert.True(result.Succeeded);
            Assert.Empty(result.Data!.SubjectVariables);
            Assert.Single(result.Data.ContentVariables);
            Assert.Contains("Message", result.Data.ContentVariables);
        }

        [Fact]
        public async Task GetTemplateVariablesAsync_HandlesMultipleSameVariablesInContent()
        {
            // Arrange
            var id = Guid.NewGuid();
            var template = new TemplateEntity
            {
                Id = id,
                TemplateName = "RepeatedVars",
                Module = "Notification",
                SubjectTemplate = "",
                ContentTemplate = "<p>@Model.Name is @Model.Name and age is @Model.Age</p>"
            };

            _repositoryMock.Setup(r => r.GetAsync(id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(template);

            // Act
            var result = await _service.GetTemplateVariablesAsync(id);

            // Assert
            Assert.True(result.Succeeded);
            // 即使 Name 出现两次，也只会被计入一次
            Assert.Equal(2, result.Data!.ContentVariables.Count);
            Assert.Contains("Name", result.Data.ContentVariables);
            Assert.Contains("Age", result.Data.ContentVariables);
        }

        [Fact]
        public async Task GetTemplateVariablesAsync_VariablesAreSorted()
        {
            // Arrange
            var id = Guid.NewGuid();
            var template = new TemplateEntity
            {
                Id = id,
                TemplateName = "SortedVars",
                Module = "Notification",
                SubjectTemplate = "",
                ContentTemplate = "<p>@Model.Zebra @Model.Apple @Model.Mango</p>"
            };

            _repositoryMock.Setup(r => r.GetAsync(id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(template);

            // Act
            var result = await _service.GetTemplateVariablesAsync(id);

            // Assert
            Assert.True(result.Succeeded);
            Assert.Equal(3, result.Data!.ContentVariables.Count);
            Assert.Equal("Apple", result.Data.ContentVariables[0]);
            Assert.Equal("Mango", result.Data.ContentVariables[1]);
            Assert.Equal("Zebra", result.Data.ContentVariables[2]);
        }

        #endregion

        #region E1-3: Template BatchActivateAsync Tests

        [Fact]
        public async Task BatchActivateAsync_ActivatesMultipleTemplates()
        {
            // Arrange
            var id1 = Guid.NewGuid();
            var id2 = Guid.NewGuid();
            var templates = new List<TemplateEntity>
            {
                new() { Id = id1, TemplateName = "Template1", Module = "Notification", IsActive = false, IsDeleted = false },
                new() { Id = id2, TemplateName = "Template2", Module = "Notification", IsActive = false, IsDeleted = false }
            };

            SetupQueryable(templates);

            // Act
            var result = await _service.BatchActivateAsync(new List<Guid> { id1, id2 }, isActive: true);

            // Assert
            Assert.True(result.Succeeded);
            Assert.Equal(2, result.Data);
            _repositoryMock.Verify(r => r.UpdateManyAsync(
                It.IsAny<IEnumerable<TemplateEntity>>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task BatchActivateAsync_DeactivatesMultipleTemplates()
        {
            // Arrange
            var id1 = Guid.NewGuid();
            var id2 = Guid.NewGuid();
            var templates = new List<TemplateEntity>
            {
                new() { Id = id1, TemplateName = "Template1", Module = "Notification", IsActive = true, IsDeleted = false },
                new() { Id = id2, TemplateName = "Template2", Module = "Notification", IsActive = true, IsDeleted = false }
            };

            SetupQueryable(templates);

            // Act
            var result = await _service.BatchActivateAsync(new List<Guid> { id1, id2 }, isActive: false);

            // Assert
            Assert.True(result.Succeeded);
            Assert.Equal(2, result.Data);
            _repositoryMock.Verify(r => r.UpdateManyAsync(
                It.IsAny<IEnumerable<TemplateEntity>>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task BatchActivateAsync_AlreadyInTargetState_ReturnsZeroChanged()
        {
            // Arrange
            var id1 = Guid.NewGuid();
            var templates = new List<TemplateEntity>
            {
                new() { Id = id1, TemplateName = "AlreadyActive", Module = "Notification", IsActive = true, IsDeleted = false }
            };

            SetupQueryable(templates);

            // Act - 尝试激活一个已经是激活状态的模板
            var result = await _service.BatchActivateAsync(new List<Guid> { id1 }, isActive: true);

            // Assert
            Assert.True(result.Succeeded);
            Assert.Equal(0, result.Data);
            // 无变更时不应调用 UpdateManyAsync
            _repositoryMock.Verify(r => r.UpdateManyAsync(
                It.IsAny<IEnumerable<TemplateEntity>>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task BatchActivateAsync_NonExistentIds_ReturnsFailResult()
        {
            // Arrange
            SetupQueryable(new List<TemplateEntity>());

            // Act
            var result = await _service.BatchActivateAsync(new List<Guid> { Guid.NewGuid() }, isActive: true);

            // Assert
            Assert.False(result.Succeeded);
            Assert.Equal(404, result.Code);
        }

        [Fact]
        public async Task BatchActivateAsync_ExcludesDeletedTemplates()
        {
            // Arrange
            var id1 = Guid.NewGuid();
            var id2 = Guid.NewGuid();
            var templates = new List<TemplateEntity>
            {
                new() { Id = id1, TemplateName = "ActiveTemplate", Module = "Notification", IsActive = false, IsDeleted = false },
                new() { Id = id2, TemplateName = "DeletedTemplate", Module = "Notification", IsActive = false, IsDeleted = true }
            };

            SetupQueryable(templates);

            // Act
            var result = await _service.BatchActivateAsync(new List<Guid> { id1, id2 }, isActive: true);

            // Assert
            Assert.True(result.Succeeded);
            // 只有非删除的模板会被修改
            Assert.Equal(1, result.Data);
        }

        [Fact]
        public async Task BatchActivateAsync_MixedStates_OnlyChangesNeeded()
        {
            // Arrange
            var id1 = Guid.NewGuid();
            var id2 = Guid.NewGuid();
            var id3 = Guid.NewGuid();
            var templates = new List<TemplateEntity>
            {
                new() { Id = id1, TemplateName = "Inactive1", Module = "Notification", IsActive = false, IsDeleted = false },
                new() { Id = id2, TemplateName = "AlreadyActive", Module = "Notification", IsActive = true, IsDeleted = false },
                new() { Id = id3, TemplateName = "Inactive2", Module = "Notification", IsActive = false, IsDeleted = false }
            };

            SetupQueryable(templates);

            // Act
            var result = await _service.BatchActivateAsync(new List<Guid> { id1, id2, id3 }, isActive: true);

            // Assert
            Assert.True(result.Succeeded);
            Assert.Equal(2, result.Data); // 只有2个需要变更
        }

        #endregion
    }

    #endregion
}
