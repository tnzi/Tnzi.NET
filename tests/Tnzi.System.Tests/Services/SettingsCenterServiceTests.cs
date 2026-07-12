namespace Tnzi.System.Tests.Services;

using Tnzi.Security.Authorization;

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
    public async Task SaveGroup_Duration_Should_Reject_Invalid_And_Accept_Canonical_TimeSpan()
    {
        _providers.Add(new FakeProvider(DemoGroup(
            new SettingFieldDefinition { Key = "Demo:Ttl", Label = "TTL", Type = SettingFieldType.Duration })));
        var service = CreateService();

        // 非法时长 → 400。
        (await service.SaveGroupAsync("demo", new Dictionary<string, string?> { ["Demo:Ttl"] = "not-a-duration" })).Code.ShouldBe(400);

        // canonical TimeSpan 字符串往返写入。
        _settingServiceMock
            .Setup(s => s.SetSettingAsync("Demo:Ttl", "00:05:00", It.IsAny<string?>(), "demo"))
            .ReturnsAsync(Result.Success());
        (await service.SaveGroupAsync("demo", new Dictionary<string, string?> { ["Demo:Ttl"] = "00:05:00" })).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task SaveGroup_Should_Reject_Pattern_Mismatch()
    {
        _providers.Add(new FakeProvider(DemoGroup(
            new SettingFieldDefinition { Key = "Demo:Url", Label = "U", Type = SettingFieldType.String, Pattern = "https?://.+" })));
        var service = CreateService();

        (await service.SaveGroupAsync("demo", new Dictionary<string, string?> { ["Demo:Url"] = "not-a-url" })).Code.ShouldBe(400);
        _settingServiceMock
            .Setup(s => s.SetSettingAsync("Demo:Url", "https://ok.example", It.IsAny<string?>(), "demo"))
            .ReturnsAsync(Result.Success());
        (await service.SaveGroupAsync("demo", new Dictionary<string, string?> { ["Demo:Url"] = "https://ok.example" })).Succeeded.ShouldBeTrue();
    }

    [ConfigSection("Demo")]
    public sealed class CrossFieldOptions
    {
        public int A { get; set; }
        public int B { get; set; }
    }

    public sealed class CrossFieldValidator : IValidateOptions<CrossFieldOptions>
    {
        public ValidateOptionsResult Validate(string? name, CrossFieldOptions options)
            => options.B >= options.A ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail("B must be >= A");
    }

    private void SetupCrossFieldGroup()
    {
        _providers.Add(new FakeProvider(new SettingDefinitionGroup
        {
            Key = "demo",
            ModuleName = "Demo",
            DisplayName = "Demo Group",
            OptionsTypes = [typeof(CrossFieldOptions)],
            Fields =
            [
                new SettingFieldDefinition { Key = "Demo:A", Label = "A", Type = SettingFieldType.Int },
                new SettingFieldDefinition { Key = "Demo:B", Label = "B", Type = SettingFieldType.Int },
            ],
        }));
        // 非泛型 GetServices(Type) 底层解析 IEnumerable<IValidateOptions<T>>
        _serviceProviderMock
            .Setup(x => x.GetService(typeof(IEnumerable<IValidateOptions<CrossFieldOptions>>)))
            .Returns(new IValidateOptions<CrossFieldOptions>[] { new CrossFieldValidator() });
    }

    [Fact]
    public async Task SaveGroup_Should_Reject_Cross_Field_Violation_Via_Options_Validator()
    {
        // 回归：字段级校验放行的跨字段非法组合（B < A）必须在写入前被模块自己的
        // IValidateOptions 预检拦下 — 否则持久化后 reload 重绑定抛 OptionsValidationException。
        SetupCrossFieldGroup();

        var result = await CreateService().SaveGroupAsync("demo", new Dictionary<string, string?>
        {
            ["Demo:A"] = "5",
            ["Demo:B"] = "3",
        });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message.ShouldContain("B must be >= A");
        _settingServiceMock.Verify(s => s.SetSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task SaveGroup_Should_Pass_Options_Validator_With_Valid_Candidate()
    {
        SetupCrossFieldGroup();
        _settingServiceMock
            .Setup(s => s.SetSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), "demo"))
            .ReturnsAsync(Result.Success());

        var result = await CreateService().SaveGroupAsync("demo", new Dictionary<string, string?>
        {
            ["Demo:A"] = "2",
            ["Demo:B"] = "7",
        });

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task SaveGroup_Validator_Candidate_Should_Merge_Existing_Effective_Values()
    {
        // 只改 B 时，候选实例的 A 必须来自当前生效配置（而非类型默认 0）—— 否则
        // 合法请求会被误拒 / 非法请求会被误放。
        SetupCrossFieldGroup();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Demo:A"] = "5" })
            .Build();
        var service = new SettingsCenterService(
            _serviceProviderMock.Object, _settingServiceMock.Object, _repositoryMock.Object,
            config, _providers, _handlers);

        // B=3 < 生效 A=5 → 拒
        (await service.SaveGroupAsync("demo", new Dictionary<string, string?> { ["Demo:B"] = "3" })).Code.ShouldBe(400);

        // B=9 ≥ 生效 A=5 → 过
        _settingServiceMock
            .Setup(s => s.SetSettingAsync("Demo:B", "9", It.IsAny<string?>(), "demo"))
            .ReturnsAsync(Result.Success());
        (await service.SaveGroupAsync("demo", new Dictionary<string, string?> { ["Demo:B"] = "9" })).Succeeded.ShouldBeTrue();
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

    // ── 按组授权（配置中心细粒度权限）────────────────────────────────────────
    // 每个配置组由自己的 {group}.settings.{slug}.view/update 码把守；
    // PermissionChecker 为 null（Authorization 未加载）时上面所有测试走 fail-open。

    private Mock<IPermissionChecker> SetupPermissionChecker()
    {
        var checker = new Mock<IPermissionChecker>();
        _serviceProviderMock.Setup(x => x.GetService(typeof(IPermissionChecker))).Returns(checker.Object);
        return checker;
    }

    private void AddChatAndAiGroups()
    {
        _providers.Add(new FakeProvider(
            new SettingDefinitionGroup
            {
                Key = "chat-general", ModuleName = "Chat", DisplayName = "Chat",
                Fields = [new SettingFieldDefinition { Key = "Chat:X", Label = "X" }],
            },
            new SettingDefinitionGroup
            {
                Key = "ai-budget", ModuleName = "AI", DisplayName = "AI Budget",
                Fields = [new SettingFieldDefinition { Key = "AI:B", Label = "B" }],
            }));
    }

    [Fact]
    public async Task GetDefinitions_Should_Return_Only_Groups_The_User_Can_View()
    {
        AddChatAndAiGroups();
        var checker = SetupPermissionChecker();
        // Grant chat view only; ai view stays denied (Moq default false).
        checker.Setup(c => c.IsGrantedAsync("chat.settings.general.view")).ReturnsAsync(true);

        var result = await CreateService().GetDefinitionsAsync();

        result.Succeeded.ShouldBeTrue();
        result.Data!.Select(g => g.Key).ShouldBe(new[] { "chat-general" });
    }

    [Fact]
    public async Task GetDefinitions_CanEdit_Should_Reflect_Update_Permission()
    {
        AddChatAndAiGroups();
        var checker = SetupPermissionChecker();
        checker.Setup(c => c.IsGrantedAsync("chat.settings.general.view")).ReturnsAsync(true);
        checker.Setup(c => c.IsGrantedAsync("ai.settings.budget.view")).ReturnsAsync(true);
        // Chat editable, AI view-only.
        checker.Setup(c => c.IsGrantedAsync("chat.settings.general.update")).ReturnsAsync(true);

        var result = await CreateService().GetDefinitionsAsync();

        result.Data!.Single(g => g.Key == "chat-general").CanEdit.ShouldBeTrue();
        result.Data!.Single(g => g.Key == "ai-budget").CanEdit.ShouldBeFalse();
    }

    [Fact]
    public async Task GetDefinitions_Should_Be_Empty_When_User_Holds_No_Settings_Permission()
    {
        AddChatAndAiGroups();
        SetupPermissionChecker(); // grants nothing

        var result = await CreateService().GetDefinitionsAsync();

        result.Succeeded.ShouldBeTrue();
        result.Data!.ShouldBeEmpty();
    }

    [Fact]
    public async Task SaveGroup_Should_403_When_User_Lacks_Update_Permission()
    {
        AddChatAndAiGroups();
        SetupPermissionChecker(); // update denied

        var result = await CreateService().SaveGroupAsync("chat-general", new Dictionary<string, string?> { ["Chat:X"] = "v" });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(403);
        _settingServiceMock.Verify(
            s => s.SetSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()),
            Times.Never);
    }

    [Fact]
    public async Task ResetGroup_Should_403_When_User_Lacks_Update_Permission()
    {
        AddChatAndAiGroups();
        SetupPermissionChecker(); // update denied

        var result = await CreateService().ResetGroupAsync("chat-general");

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(403);
        _settingServiceMock.Verify(s => s.DeleteSettingAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task SaveGroup_Should_Succeed_When_Update_Permission_Granted()
    {
        AddChatAndAiGroups();
        var checker = SetupPermissionChecker();
        checker.Setup(c => c.IsGrantedAsync("chat.settings.general.update")).ReturnsAsync(true);
        _settingServiceMock
            .Setup(s => s.SetSettingAsync("Chat:X", "v", It.IsAny<string?>(), "chat-general"))
            .ReturnsAsync(Result.Success());

        var result = await CreateService().SaveGroupAsync("chat-general", new Dictionary<string, string?> { ["Chat:X"] = "v" });

        result.Succeeded.ShouldBeTrue();
        _settingServiceMock.Verify(s => s.SetSettingAsync("Chat:X", "v", It.IsAny<string?>(), "chat-general"), Times.Once);
    }
}
