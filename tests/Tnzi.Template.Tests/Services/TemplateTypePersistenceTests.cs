namespace Tnzi.Template.Tests.Services;

/// <summary>
/// <c>Template.Type</c> 自 2026-09-12 起是编码开关（<c>Sms</c> = 正文纯文本，其余 HTML），于是每一条
/// 写它的持久化路径都必须把它带过去：管理端编辑、导出/导入、克隆。
/// </summary>
/// <remarks>
/// <b>被保护的缺陷</b>：请求 DTO 的 <c>Type</c> 默认 <c>Generic</c> 而管理端表单从不发它，
/// 导出条目没有 <c>Type</c>，克隆逐字段复制却漏掉它 —— 一份经 API 建成 <c>Sms</c> 的模板，
/// 被一次普通编辑、一次导出再导入、或一次克隆静默改回 HTML 编码，短信里的
/// <c>?token=x&amp;uid=y</c> 又变回坏链接，发送状态成功、无日志。
/// </remarks>
public class TemplateTypePersistenceTests
{
    private readonly Mock<IRepository<Entities.Template, Guid>> _repositoryMock;
    private readonly TemplateStoreService _service;

    public TemplateTypePersistenceTests()
    {
        MapperExtensions.SetMapper(new Mapper(new TypeAdapterConfig()));

        _repositoryMock = new Mock<IRepository<Entities.Template, Guid>>();
        var serviceProviderMock = new Mock<IServiceProvider>();
        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
        serviceProviderMock.Setup(sp => sp.GetService(typeof(ILoggerFactory))).Returns(loggerFactory.Object);

        _service = new TemplateStoreService(_repositoryMock.Object, serviceProviderMock.Object);
    }

    private static Entities.Template SmsTemplate(string name = "Reset") => new()
    {
        Id = Guid.NewGuid(),
        TemplateName = name,
        Module = "Notification",
        Category = "Sms",
        Type = TemplateType.Sms,
        SubjectTemplate = string.Empty,
        ContentTemplate = "Reset: @Model.Url",
        IsActive = true
    };

    private void SetupQueryable(List<Entities.Template> templates)
    {
        var mockQueryable = templates.BuildMock();
        _repositoryMock.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(mockQueryable);
        _repositoryMock.As<IQueryable<Entities.Template>>().Setup(q => q.Provider).Returns(mockQueryable.Provider);
        _repositoryMock.As<IQueryable<Entities.Template>>().Setup(q => q.Expression).Returns(mockQueryable.Expression);
        _repositoryMock.As<IQueryable<Entities.Template>>().Setup(q => q.ElementType).Returns(mockQueryable.ElementType);
        _repositoryMock.As<IQueryable<Entities.Template>>().Setup(q => q.GetEnumerator()).Returns(() => mockQueryable.GetEnumerator());
    }

    #region 管理端编辑

    [Fact]
    public async Task Update_WithoutType_KeepsExistingType()
    {
        var existing = SmsTemplate();
        _repositoryMock.Setup(r => r.GetAsync(existing.Id, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        _repositoryMock.Setup(r => r.UpdateAsync(It.IsAny<Entities.Template>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        // 管理端表单送来的就是这样一份请求：改了正文，没提类型
        var request = new UpdateTemplateRequest
        {
            TemplateName = existing.TemplateName,
            Module = existing.Module,
            Category = existing.Category,
            ContentTemplate = "Reset your password: @Model.Url"
        };

        var result = await _service.UpdateTemplateAsync(existing.Id, request);

        Assert.True(result.Succeeded);
        Assert.Equal(TemplateType.Sms, existing.Type);
        Assert.Equal(TemplateType.Sms, result.Data!.Type);
    }

    [Fact]
    public async Task Update_WithExplicitType_ChangesIt()
    {
        var existing = SmsTemplate();
        _repositoryMock.Setup(r => r.GetAsync(existing.Id, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        _repositoryMock.Setup(r => r.UpdateAsync(It.IsAny<Entities.Template>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var request = new UpdateTemplateRequest
        {
            TemplateName = existing.TemplateName,
            Module = existing.Module,
            Category = existing.Category,
            ContentTemplate = existing.ContentTemplate,
            Type = TemplateType.Email
        };

        var result = await _service.UpdateTemplateAsync(existing.Id, request);

        Assert.True(result.Succeeded);
        Assert.Equal(TemplateType.Email, existing.Type);
    }

    [Fact]
    public async Task Create_WithoutType_DefaultsToGeneric()
    {
        Entities.Template? inserted = null;
        _repositoryMock.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<Entities.Template, bool>>>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _repositoryMock.Setup(r => r.InsertAsync(It.IsAny<Entities.Template>(), It.IsAny<CancellationToken>()))
            .Callback<Entities.Template, CancellationToken>((t, _) => inserted = t)
            .Returns(Task.CompletedTask);

        var result = await _service.CreateTemplateAsync(new CreateTemplateRequest
        {
            TemplateName = "Welcome",
            Module = "Notification",
            Category = "Email",
            ContentTemplate = "<p>Hi</p>"
        });

        Assert.True(result.Succeeded);
        Assert.Equal(TemplateType.Generic, inserted!.Type);
    }

    [Fact]
    public async Task Create_WithType_PersistsIt()
    {
        Entities.Template? inserted = null;
        _repositoryMock.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<Entities.Template, bool>>>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _repositoryMock.Setup(r => r.InsertAsync(It.IsAny<Entities.Template>(), It.IsAny<CancellationToken>()))
            .Callback<Entities.Template, CancellationToken>((t, _) => inserted = t)
            .Returns(Task.CompletedTask);

        var result = await _service.CreateTemplateAsync(new CreateTemplateRequest
        {
            TemplateName = "Reset",
            Module = "Notification",
            Category = "Sms",
            ContentTemplate = "@Model.Url",
            Type = TemplateType.Sms
        });

        Assert.True(result.Succeeded);
        Assert.Equal(TemplateType.Sms, inserted!.Type);
    }

    #endregion

    #region 导出 / 导入

    [Fact]
    public async Task Export_CarriesType_AsMemberName()
    {
        SetupQueryable([SmsTemplate()]);

        var result = await _service.ExportTemplatesAsync();

        Assert.True(result.Succeeded);
        using var doc = JsonDocument.Parse(result.Data!);
        var entry = doc.RootElement[0];
        Assert.Equal("Sms", entry.GetProperty("type").GetString());
    }

    [Fact]
    public async Task Import_Insert_SetsType()
    {
        const string json = """
        [
            { "templateName": "Reset", "module": "Notification", "category": "Sms", "contentTemplate": "@Model.Url", "type": "Sms" }
        ]
        """;
        List<Entities.Template>? inserted = null;
        _repositoryMock.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<Entities.Template, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Entities.Template?)null);
        _repositoryMock.Setup(r => r.InsertManyAsync(It.IsAny<IEnumerable<Entities.Template>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<Entities.Template>, CancellationToken>((t, _) => inserted = t.ToList())
            .Returns(Task.CompletedTask);

        var result = await _service.ImportTemplatesAsync(json);

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.Data!.CreatedCount);
        Assert.Equal(TemplateType.Sms, Assert.Single(inserted!).Type);
    }

    [Fact]
    public async Task Import_Insert_AcceptsNumericType()
    {
        // 老导出文件与手写文件可能用数字；JsonStringEnumConverter 默认两种都认
        const string json = """
        [
            { "templateName": "Reset", "module": "Notification", "category": "Sms", "contentTemplate": "@Model.Url", "type": 2 }
        ]
        """;
        List<Entities.Template>? inserted = null;
        _repositoryMock.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<Entities.Template, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Entities.Template?)null);
        _repositoryMock.Setup(r => r.InsertManyAsync(It.IsAny<IEnumerable<Entities.Template>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<Entities.Template>, CancellationToken>((t, _) => inserted = t.ToList())
            .Returns(Task.CompletedTask);

        var result = await _service.ImportTemplatesAsync(json);

        Assert.True(result.Succeeded);
        Assert.Equal(TemplateType.Sms, Assert.Single(inserted!).Type);
    }

    [Fact]
    public async Task Import_Overwrite_SetsType()
    {
        const string json = """
        [
            { "templateName": "Reset", "module": "Notification", "category": "Sms", "contentTemplate": "@Model.Url", "type": "Sms" }
        ]
        """;
        var existing = SmsTemplate();
        existing.Type = TemplateType.Generic;
        _repositoryMock.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<Entities.Template, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _repositoryMock.Setup(r => r.UpdateAsync(It.IsAny<Entities.Template>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var result = await _service.ImportTemplatesAsync(json, overwriteExisting: true);

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.Data!.UpdatedCount);
        Assert.Equal(TemplateType.Sms, existing.Type);
    }

    [Fact]
    public async Task Import_Overwrite_WithoutType_KeepsExistingType()
    {
        // 机制诞生前导出的文件没有 type；覆盖导入不能把已经是 Sms 的行改回 Generic
        const string json = """
        [
            { "templateName": "Reset", "module": "Notification", "category": "Sms", "contentTemplate": "@Model.Url" }
        ]
        """;
        var existing = SmsTemplate();
        _repositoryMock.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<Entities.Template, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _repositoryMock.Setup(r => r.UpdateAsync(It.IsAny<Entities.Template>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var result = await _service.ImportTemplatesAsync(json, overwriteExisting: true);

        Assert.True(result.Succeeded);
        Assert.Equal(TemplateType.Sms, existing.Type);
    }

    [Fact]
    public async Task Export_Then_Import_RoundTripsType()
    {
        SetupQueryable([SmsTemplate()]);
        var exported = await _service.ExportTemplatesAsync();

        List<Entities.Template>? inserted = null;
        _repositoryMock.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Expression<Func<Entities.Template, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Entities.Template?)null);
        _repositoryMock.Setup(r => r.InsertManyAsync(It.IsAny<IEnumerable<Entities.Template>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<Entities.Template>, CancellationToken>((t, _) => inserted = t.ToList())
            .Returns(Task.CompletedTask);

        var imported = await _service.ImportTemplatesAsync(exported.Data!);

        Assert.True(imported.Succeeded);
        Assert.Equal(TemplateType.Sms, Assert.Single(inserted!).Type);
    }

    #endregion

    #region 克隆

    [Fact]
    public async Task Clone_CopiesType()
    {
        var source = SmsTemplate();
        Entities.Template? inserted = null;
        _repositoryMock.Setup(r => r.GetAsync(source.Id, It.IsAny<CancellationToken>())).ReturnsAsync(source);
        _repositoryMock.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<Entities.Template, bool>>>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _repositoryMock.Setup(r => r.InsertAsync(It.IsAny<Entities.Template>(), It.IsAny<CancellationToken>()))
            .Callback<Entities.Template, CancellationToken>((t, _) => inserted = t)
            .Returns(Task.CompletedTask);

        var result = await _service.CloneTemplateAsync(source.Id, "ResetCopy");

        Assert.True(result.Succeeded);
        Assert.Equal(TemplateType.Sms, inserted!.Type);
    }

    #endregion
}
