
namespace Tnzi.Storage.Tests.Integration;

/// <summary>
/// 覆盖 T7（EnableMd5Validation / EnableFileReference / UrlPrefix 热更新）三项配置生效的集成测试。
/// T6（分片合并回读分片 MD5 校验）随分片上传服务搬到了 Tnzi.Storage.Workspace.Tests。
/// </summary>
public class StorageConfigToggleTests : StorageIntegrationTestBase
{
    // ---------------------------------------------------------------------
    // T7.1: EnableMd5Validation 关闭时禁用按 MD5 去重
    // ---------------------------------------------------------------------

    [Fact]
    public async Task SaveAsync_WithMd5Disabled_DoesNotDeduplicate()
    {
        var options = new StorageOptions
        {
            MaxFileSize = StorageOptions.MaxFileSize,
            AllowedExtensions = StorageOptions.AllowedExtensions,
            AutoGenerateThumbnail = false,
            EnableMd5Validation = false
        };
        var service = CreateStorageService(options);

        var r1 = await service.SaveAsync("a.txt", new MemoryStream("same-content"u8.ToArray()));
        var r2 = await service.SaveAsync("b.txt", new MemoryStream("same-content"u8.ToArray()));

        Assert.True(r1.Succeeded);
        Assert.True(r2.Succeeded);
        // 两次相同内容产生两条独立记录（无去重）
        Assert.NotEqual(r1.Data!.Id, r2.Data!.Id);
        Assert.Equal(2, DbContext.FileRecords.Count());
    }

    [Fact]
    public async Task SaveAsync_WithMd5Enabled_Deduplicates()
    {
        var service = CreateStorageService(); // EnableMd5Validation = true (默认)

        var r1 = await service.SaveAsync("a.txt", new MemoryStream("dup-content"u8.ToArray()));
        // 清除变更跟踪，避免同一 DbContext 内首次插入实例与去重路径的 Find/Update 实例冲突
        DbContext.ChangeTracker.Clear();
        var r2 = await service.SaveAsync("b.txt", new MemoryStream("dup-content"u8.ToArray()));

        Assert.True(r1.Succeeded);
        Assert.True(r2.Succeeded);
        // 相同内容被去重，复用同一条记录
        Assert.Equal(r1.Data!.Id, r2.Data!.Id);
        Assert.Single(DbContext.FileRecords);
    }

    // ---------------------------------------------------------------------
    // T7.2: EnableFileReference 关闭时跳过引用追踪
    // ---------------------------------------------------------------------

    [Fact]
    public async Task ProcessChanges_WithFileReferenceDisabled_SkipsReferenceCreation()
    {
        var file = await CreateStoredFileAsync("ref-off.txt", "x"u8.ToArray());

        var options = new StorageOptions
        {
            MaxFileSize = StorageOptions.MaxFileSize,
            AllowedExtensions = StorageOptions.AllowedExtensions,
            AutoGenerateThumbnail = false,
            EnableFileReference = false
        };
        var processor = CreateReferenceProcessor(options);

        var changes = new List<FileReferenceChange>
        {
            new()
            {
                ChangeType = FileReferenceChangeType.Create,
                FileId = file.Id,
                EntityType = "TestEntity",
                EntityId = Guid.NewGuid().ToString(),
                FieldName = "Cover"
            }
        };

        await processor.ProcessChangesAsync(changes);

        // 引用追踪被跳过：不创建 FileReference
        Assert.Empty(DbContext.FileReferences);
    }

    [Fact]
    public async Task ProcessChanges_WithFileReferenceEnabled_CreatesReference()
    {
        var file = await CreateStoredFileAsync("ref-on.txt", "y"u8.ToArray());
        var processor = CreateReferenceProcessor(); // EnableFileReference = true (默认)

        var changes = new List<FileReferenceChange>
        {
            new()
            {
                ChangeType = FileReferenceChangeType.Create,
                FileId = file.Id,
                EntityType = "TestEntity",
                EntityId = Guid.NewGuid().ToString(),
                FieldName = "Cover"
            }
        };

        await processor.ProcessChangesAsync(changes);

        Assert.Single(DbContext.FileReferences);
    }

    // ---------------------------------------------------------------------
    // T7.3: UrlPrefix 热更新（IOptionsMonitor.CurrentValue 读取路径）
    // ---------------------------------------------------------------------

    [Fact]
    public async Task LocalStorage_GetUrl_ReflectsHotUpdatedUrlPrefix()
    {
        var live = new StorageOptions { UrlPrefix = "https://cdn-v1.example.com" };
        var monitor = new Mock<IOptionsMonitor<StorageOptions>>();
        monitor.Setup(x => x.CurrentValue).Returns(() => live);

        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:StoragePath"] = Path.Combine(Path.GetTempPath(), "tnzi-urlprefix-test", Guid.NewGuid().ToString("N"))
            })
            .Build();

        var storage = new Tnzi.Storage.Providers.LocalStorage(config, logger: NullLogger<Tnzi.Storage.Providers.LocalStorage>.Instance, optionsMonitor: monitor.Object);

        var url1 = await storage.GetUrlAsync("2026/06/20/file.txt");
        Assert.Equal("https://cdn-v1.example.com/2026/06/20/file.txt", url1);

        // 热更新 UrlPrefix → GetUrlAsync 立即反映新值
        live = new StorageOptions { UrlPrefix = "https://cdn-v2.example.com" };
        var url2 = await storage.GetUrlAsync("2026/06/20/file.txt");
        Assert.Equal("https://cdn-v2.example.com/2026/06/20/file.txt", url2);
    }

    [Fact]
    public async Task LocalStorage_GetUrl_WithoutMonitor_UsesConstructionTimePrefix()
    {
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:StoragePath"] = Path.Combine(Path.GetTempPath(), "tnzi-urlprefix-test", Guid.NewGuid().ToString("N")),
                ["Storage:UrlPrefix"] = "https://static.example.com"
            })
            .Build();

        // 不传 monitor → 回退到构造期读取的 _baseUrl（向后兼容）
        var storage = new Tnzi.Storage.Providers.LocalStorage(config, logger: NullLogger<Tnzi.Storage.Providers.LocalStorage>.Instance);

        var url = await storage.GetUrlAsync("a/b.txt");
        Assert.Equal("https://static.example.com/a/b.txt", url);
    }
}
