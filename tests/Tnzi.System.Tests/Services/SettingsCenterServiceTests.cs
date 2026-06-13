namespace Tnzi.System.Tests.Services;

public class SettingsCenterServiceTests
{
    private readonly Mock<IRepository<Setting, Guid>> _repositoryMock = new();
    private readonly Mock<ISettingService> _settingServiceMock = new();
    private readonly Mock<IServiceProvider> _serviceProviderMock = new();
    private readonly List<ISettingDefinitionProvider> _providers = new();
    private readonly List<ISettingGroupHandler> _handlers = new();
    private readonly IConfiguration _configuration;

    public SettingsCenterServiceTests()
    {
        var loggerFactoryMock = new Mock<ILoggerFactory>();
        loggerFactoryMock.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
        _serviceProviderMock.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactoryMock.Object);

        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Demo:FromAppSettings"] = "from-config" })
            .Build();

        _repositoryMock.Setup(r => r.ToListAsync(null, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Setting>());
    }

    private SettingsCenterService CreateService() => new(
        _serviceProviderMock.Object,
        _settingServiceMock.Object,
        _repositoryMock.Object,
        _configuration,
        _providers,
        _handlers);

    private sealed class FakeProvider(params SettingDefinitionGroup[] groups) : ISettingDefinitionProvider
    {
        public IReadOnlyList<SettingDefinitionGroup> GetGroups() => groups;
    }

    private static SettingDefinitionGroup DemoGroup(params SettingFieldDefinition[] fields) => new()
    {
        Key = "demo",
        ModuleName = "Demo",
        DisplayName = "Demo Group",
        Fields = fields,
    };

    [Fact]
    public async Task GetDefinitions_Should_Aggregate_Providers_Ordered()
    {
        _providers.Add(new FakeProvider(new SettingDefinitionGroup
        {
            Key = "b-group", ModuleName = "B", DisplayName = "B", Order = 20,
            Fields = [new SettingFieldDefinition { Key = "B:X", Label = "X" }],
        }));
        _providers.Add(new FakeProvider(new SettingDefinitionGroup
        {
            Key = "a-group", ModuleName = "A", DisplayName = "A", Order = 10,
            Fields = [new SettingFieldDefinition { Key = "A:X", Label = "X" }],
        }));

        var result = await CreateService().GetDefinitionsAsync();

        result.Succeeded.ShouldBeTrue();
        result.Data!.Select(g => g.Key).ShouldBe(new[] { "a-group", "b-group" });
    }

    [Fact]
    public async Task GetDefinitions_Value_Should_Prefer_Override_Then_AppSettings_Then_CompiledDefault()
    {
        _providers.Add(new FakeProvider(DemoGroup(
            new SettingFieldDefinition { Key = "Demo:Overridden", Label = "O" },
            new SettingFieldDefinition { Key = "Demo:FromAppSettings", Label = "C" },
            new SettingFieldDefinition { Key = "Demo:Compiled", Label = "D", DefaultValueAccessor = () => "compiled-default" })));
        _repositoryMock.Setup(r => r.ToListAsync(null, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Setting>
        {
            new() { Id = Guid.NewGuid(), Key = "Demo:Overridden", Value = "db-value", Scope = SettingScope.Global },
        });

        var result = await CreateService().GetDefinitionsAsync();

        var fields = result.Data![0].Fields;
        var overridden = fields.Single(f => f.Key == "Demo:Overridden");
        overridden.Value.ShouldBe("db-value");
        overridden.IsOverridden.ShouldBeTrue();
        var fromConfig = fields.Single(f => f.Key == "Demo:FromAppSettings");
        fromConfig.Value.ShouldBe("from-config");
        fromConfig.DefaultValue.ShouldBe("from-config");
        fromConfig.IsOverridden.ShouldBeFalse();
        fields.Single(f => f.Key == "Demo:Compiled").Value.ShouldBe("compiled-default");
    }

    [Fact]
    public async Task GetDefinitions_Encrypted_Field_Should_Not_Return_Value_But_IsSet()
    {
        _providers.Add(new FakeProvider(DemoGroup(
            new SettingFieldDefinition { Key = "Demo:Secret", Label = "S", Type = SettingFieldType.Password })));
        _repositoryMock.Setup(r => r.ToListAsync(null, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Setting>
        {
            new() { Id = Guid.NewGuid(), Key = "Demo:Secret", Value = "enc:abc", Scope = SettingScope.Global, IsEncrypted = true },
        });

        var result = await CreateService().GetDefinitionsAsync();

        var field = result.Data![0].Fields.Single();
        field.Value.ShouldBeNull();
        field.IsSet.ShouldBeTrue();
        field.IsEncrypted.ShouldBeTrue();
    }

    [Fact]
    public async Task SaveGroup_Should_Reject_Unknown_Group()
    {
        var result = await CreateService().SaveGroupAsync("nope", new Dictionary<string, string?>());
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
    }

    [Fact]
    public async Task SaveGroup_Should_Reject_Unknown_And_ReadOnly_Fields()
    {
        _providers.Add(new FakeProvider(DemoGroup(
            new SettingFieldDefinition { Key = "Demo:Ro", Label = "Ro", IsReadOnly = true })));
        var service = CreateService();

        var unknown = await service.SaveGroupAsync("demo", new Dictionary<string, string?> { ["Demo:Nope"] = "1" });
        unknown.Succeeded.ShouldBeFalse();
        unknown.Code.ShouldBe(400);

        var readOnly = await service.SaveGroupAsync("demo", new Dictionary<string, string?> { ["Demo:Ro"] = "1" });
        readOnly.Succeeded.ShouldBeFalse();
        readOnly.Code.ShouldBe(400);
    }

    [Theory]
    [InlineData(SettingFieldType.Int, "not-int")]
    [InlineData(SettingFieldType.Decimal, "not-decimal")]
    [InlineData(SettingFieldType.Boolean, "not-bool")]
    public async Task SaveGroup_Should_Reject_Type_Mismatch(SettingFieldType type, string badValue)
    {
        _providers.Add(new FakeProvider(DemoGroup(
            new SettingFieldDefinition { Key = "Demo:F", Label = "F", Type = type })));

        var result = await CreateService().SaveGroupAsync("demo", new Dictionary<string, string?> { ["Demo:F"] = badValue });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    [Fact]
    public async Task SaveGroup_Should_Enforce_Min_Max_And_Select_Options()
    {
        _providers.Add(new FakeProvider(DemoGroup(
            new SettingFieldDefinition { Key = "Demo:N", Label = "N", Type = SettingFieldType.Int, Min = 1, Max = 10 },
            new SettingFieldDefinition { Key = "Demo:S", Label = "S", Type = SettingFieldType.Select, Options = ["a", "b"] })));
        var service = CreateService();

        (await service.SaveGroupAsync("demo", new Dictionary<string, string?> { ["Demo:N"] = "0" })).Succeeded.ShouldBeFalse();
        (await service.SaveGroupAsync("demo", new Dictionary<string, string?> { ["Demo:N"] = "11" })).Succeeded.ShouldBeFalse();
        (await service.SaveGroupAsync("demo", new Dictionary<string, string?> { ["Demo:S"] = "c" })).Succeeded.ShouldBeFalse();
    }

    [Fact]
    public async Task SaveGroup_Should_Flush_Pending_Writes_Before_Reading_Back()
    {
        // 回归：写经 ISettingService 滞留在 UoW change tracker（智能保存推迟），
        // 而回读是 AsNoTracking 直查数据库 — 不先显式 flush 则响应永远是写之前的旧值
        //（即「保存后切换分区再切回显示旧数据」的根因）。
        var calls = new List<string>();
        _providers.Add(new FakeProvider(DemoGroup(
            new SettingFieldDefinition { Key = "Demo:X", Label = "X" })));
        _settingServiceMock
            .Setup(s => s.SetSettingAsync("Demo:X", "new", It.IsAny<string?>(), "demo"))
            .Callback(() => calls.Add("write"))
            .ReturnsAsync(Result.Success());
        _repositoryMock
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("flush"))
            .ReturnsAsync(1);
        _repositoryMock
            .Setup(r => r.ToListAsync(null, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("read"))
            .ReturnsAsync(new List<Setting>());

        var result = await CreateService().SaveGroupAsync("demo", new Dictionary<string, string?> { ["Demo:X"] = "new" });

        result.Succeeded.ShouldBeTrue();
        calls.ShouldBe(new[] { "write", "flush", "read" });
    }

    [Fact]
    public async Task SaveGroup_Should_Persist_Via_SettingService_And_Run_Handler()
    {
        _providers.Add(new FakeProvider(DemoGroup(
            new SettingFieldDefinition { Key = "Demo:X", Label = "X" })));
        _settingServiceMock
            .Setup(s => s.SetSettingAsync("Demo:X", "new", It.IsAny<string?>(), "demo"))
            .ReturnsAsync(Result.Success());
        var handlerMock = new Mock<ISettingGroupHandler>();
        handlerMock.SetupGet(h => h.GroupKey).Returns("demo");
        handlerMock
            .Setup(h => h.ValidateAsync(It.IsAny<IReadOnlyDictionary<string, string?>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        handlerMock
            .Setup(h => h.OnSavedAsync(It.IsAny<IReadOnlyDictionary<string, string?>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _handlers.Add(handlerMock.Object);

        var result = await CreateService().SaveGroupAsync("demo", new Dictionary<string, string?> { ["Demo:X"] = "new" });

        result.Succeeded.ShouldBeTrue();
        _settingServiceMock.Verify(s => s.SetSettingAsync("Demo:X", "new", It.IsAny<string?>(), "demo"), Times.Once);
        handlerMock.Verify(h => h.OnSavedAsync(It.IsAny<IReadOnlyDictionary<string, string?>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SaveGroup_Should_Abort_When_Handler_Validation_Fails()
    {
        _providers.Add(new FakeProvider(DemoGroup(
            new SettingFieldDefinition { Key = "Demo:X", Label = "X" })));
        var handlerMock = new Mock<ISettingGroupHandler>();
        handlerMock.SetupGet(h => h.GroupKey).Returns("demo");
        handlerMock
            .Setup(h => h.ValidateAsync(It.IsAny<IReadOnlyDictionary<string, string?>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure("invalid cron", 400));
        _handlers.Add(handlerMock.Object);

        var result = await CreateService().SaveGroupAsync("demo", new Dictionary<string, string?> { ["Demo:X"] = "v" });

        result.Succeeded.ShouldBeFalse();
        result.Message.ShouldBe("invalid cron");
        _settingServiceMock.Verify(s => s.SetSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task SaveGroup_Null_Value_Should_Remove_Override()
    {
        var rowId = Guid.NewGuid();
        _providers.Add(new FakeProvider(DemoGroup(
            new SettingFieldDefinition { Key = "Demo:X", Label = "X" })));
        _repositoryMock.Setup(r => r.ToListAsync(null, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Setting>
        {
            new() { Id = rowId, Key = "Demo:X", Value = "old", Scope = SettingScope.Global },
        });
        _settingServiceMock.Setup(s => s.DeleteSettingAsync(rowId)).ReturnsAsync(Result.Success());

        var result = await CreateService().SaveGroupAsync("demo", new Dictionary<string, string?> { ["Demo:X"] = null });

        result.Succeeded.ShouldBeTrue();
        _settingServiceMock.Verify(s => s.DeleteSettingAsync(rowId), Times.Once);
    }

    [Fact]
    public async Task ResetGroup_Should_Delete_All_Group_Overrides()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        _providers.Add(new FakeProvider(DemoGroup(
            new SettingFieldDefinition { Key = "Demo:A", Label = "A" },
            new SettingFieldDefinition { Key = "Demo:B", Label = "B" })));
        _repositoryMock.Setup(r => r.ToListAsync(null, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Setting>
        {
            new() { Id = id1, Key = "Demo:A", Value = "1", Scope = SettingScope.Global },
            new() { Id = id2, Key = "Demo:B", Value = "2", Scope = SettingScope.Global },
        });
        _settingServiceMock.Setup(s => s.DeleteSettingAsync(It.IsAny<Guid>())).ReturnsAsync(Result.Success());

        var result = await CreateService().ResetGroupAsync("demo");

        result.Succeeded.ShouldBeTrue();
        _settingServiceMock.Verify(s => s.DeleteSettingAsync(id1), Times.Once);
        _settingServiceMock.Verify(s => s.DeleteSettingAsync(id2), Times.Once);
    }

    [Fact]
    public async Task SaveGroup_Mid_Loop_Persist_Failure_Should_Fail_With_Prior_Fields_Committed()
    {
        // 锁定部分写入语义：各字段独立持久化、中途失败不回滚（接口契约已注明，重试幂等）。
        _providers.Add(new FakeProvider(DemoGroup(
            new SettingFieldDefinition { Key = "Demo:A", Label = "A" },
            new SettingFieldDefinition { Key = "Demo:B", Label = "B" })));
        _settingServiceMock
            .Setup(s => s.SetSettingAsync("Demo:A", "1", It.IsAny<string?>(), "demo"))
            .ReturnsAsync(Result.Success());
        _settingServiceMock
            .Setup(s => s.SetSettingAsync("Demo:B", "2", It.IsAny<string?>(), "demo"))
            .ReturnsAsync(Result.Failure("db error", 500));

        var result = await CreateService().SaveGroupAsync("demo",
            new Dictionary<string, string?> { ["Demo:A"] = "1", ["Demo:B"] = "2" });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(500);
        result.Message.ShouldBe("db error");
        // Demo:A 已写入 — 证实部分写入语义
        _settingServiceMock.Verify(s => s.SetSettingAsync("Demo:A", "1", It.IsAny<string?>(), "demo"), Times.Once);
    }

    [Fact]
    public async Task SaveGroup_Should_Persist_Canonical_Field_Key_Regardless_Of_Request_Casing()
    {
        // 大小写漂移的请求键若按原样持久化，会在大小写敏感数据库里产生重复 Setting 行。
        _providers.Add(new FakeProvider(DemoGroup(
            new SettingFieldDefinition { Key = "Demo:X", Label = "X" })));
        _settingServiceMock
            .Setup(s => s.SetSettingAsync("Demo:X", "v", It.IsAny<string?>(), "demo"))
            .ReturnsAsync(Result.Success());

        var result = await CreateService().SaveGroupAsync("demo", new Dictionary<string, string?> { ["demo:x"] = "v" });

        result.Succeeded.ShouldBeTrue();
        _settingServiceMock.Verify(s => s.SetSettingAsync("Demo:X", "v", It.IsAny<string?>(), "demo"), Times.Once);
        _settingServiceMock.Verify(s => s.SetSettingAsync("demo:x", It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task SaveGroup_OnSaved_Hook_Failure_Should_Not_Fail_The_Committed_Save()
    {
        _providers.Add(new FakeProvider(DemoGroup(
            new SettingFieldDefinition { Key = "Demo:X", Label = "X" })));
        _settingServiceMock
            .Setup(s => s.SetSettingAsync("Demo:X", "v", It.IsAny<string?>(), "demo"))
            .ReturnsAsync(Result.Success());
        var handlerMock = new Mock<ISettingGroupHandler>();
        handlerMock.SetupGet(h => h.GroupKey).Returns("demo");
        handlerMock
            .Setup(h => h.ValidateAsync(It.IsAny<IReadOnlyDictionary<string, string?>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        handlerMock
            .Setup(h => h.OnSavedAsync(It.IsAny<IReadOnlyDictionary<string, string?>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("side effect boom"));
        _handlers.Add(handlerMock.Object);

        var result = await CreateService().SaveGroupAsync("demo", new Dictionary<string, string?> { ["Demo:X"] = "v" });

        // 值已提交，钩子副作用失败只记日志不报错（与事件处理器同样的隔离纪律）。
        result.Succeeded.ShouldBeTrue();
    }
}
