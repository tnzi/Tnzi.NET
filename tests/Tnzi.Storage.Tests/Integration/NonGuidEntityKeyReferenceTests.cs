using Tnzi.Domain.Entities;
using Tnzi.EFCore;
using Tnzi.Modules;

namespace Tnzi.Storage.Tests.Integration;

/// <summary>主键是雪花 long 的业务实体，带一个文件字段：引用表的 EntityId 是 Guid，存不下它。</summary>
public class LongKeyedProduct : EntityBase<long>
{
    [FileField]
    public Guid? ImageId { get; set; }
}

/// <summary>主键是字符串的业务实体，带一个文件字段。</summary>
public class StringKeyedArticle : EntityBase<string>
{
    [FileField(Public = true)]
    public Guid? CoverId { get; set; }
}

/// <summary>没有文件字段的 long 主键实体：不受这条约束管辖。</summary>
public class LongKeyedLookup : EntityBase<long>
{
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// <c>[FileField]</c> 只能长在主键是单个 <see cref="Guid"/> 的实体上：引用表 <c>FileReference.EntityId</c> 是 Guid 列。
/// </summary>
/// <remarks>
/// <para>
/// <b>被保护的缺陷</b>：变更追踪器按字符串收集 Guid / long / int / string 主键，处理器却把解析不成 Guid 的一律记一条
/// Warning 后跳过：没有引用行、计数不加。主键是 long 的实体保存成功，它引用的临时上传计数仍是 0，
/// 开着清理时过了保留期就被当成孤儿删掉，而那条记录还指着它；读权限按引用放行的解析器也永远看不到这条引用。
/// </para>
/// <para>
/// 现在两道闸门：启动时 <see cref="StorageModule"/> 扫描已注册的实体，发现这样的声明就拒绝启动并逐个点名；
/// 处理器在运行时遇到一个没注册过的这种实体，拒绝这次保存（事务回滚），不再安静地跳过。
/// </para>
/// </remarks>
public class NonGuidEntityKeyReferenceTests : StorageIntegrationTestBase
{
    private async Task<FileRecord> UploadTemporaryAsync()
    {
        var result = await CreateStorageService().SaveAsync("image.png", new MemoryStream("image bytes"u8.ToArray()), isTemporary: true);
        Assert.True(result.Succeeded, result.Message);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return result.Data!;
    }

    [Theory]
    [InlineData("42")]
    [InlineData("article-7")]
    public async Task BindingAFileToANonGuidKey_IsRefused_InsteadOfSkippedSilently(string entityId)
    {
        var file = await UploadTemporaryAsync();

        var change = new FileReferenceChange
        {
            ChangeType = FileReferenceChangeType.Create,
            EntityType = nameof(LongKeyedProduct),
            EntityId = entityId,
            FieldName = nameof(LongKeyedProduct.ImageId),
            FileId = file.Id
        };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateReferenceProcessor().ProcessChangesAsync([change]));

        Assert.Contains(nameof(LongKeyedProduct), error.Message);
        Assert.Contains("Guid", error.Message);
    }

    [Fact]
    public async Task ReleasingANonGuidKey_IsANoOp_BecauseNoReferenceRowCanExistForIt()
    {
        var file = await UploadTemporaryAsync();

        var change = new FileReferenceChange
        {
            ChangeType = FileReferenceChangeType.Delete,
            EntityType = nameof(LongKeyedProduct),
            EntityId = "42",
            FieldName = nameof(LongKeyedProduct.ImageId),
            FileId = file.Id
        };

        await CreateReferenceProcessor().ProcessChangesAsync([change]);

        Assert.Equal(0, (await DbContext.FileRecords.AsNoTracking().SingleAsync(f => f.Id == file.Id)).ReferenceCount);
    }

    [Fact]
    public void KeyGuard_NamesEveryEntityWhoseFileFieldsCannotBeTracked()
    {
        var unsupported = FileFieldHostKeyGuard.FindUnsupportedHosts(
            [typeof(LongKeyedProduct), typeof(StringKeyedArticle), typeof(LongKeyedLookup), typeof(TxTestDoc)]);

        Assert.Equal(
            [$"{nameof(LongKeyedProduct)} (key Int64)", $"{nameof(StringKeyedArticle)} (key String)"],
            unsupported.OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public async Task StartingTheModule_WithAFileFieldOnANonGuidKey_FailsAndNamesTheEntity()
    {
        await using var provider = ModuleProvider([typeof(LongKeyedProduct), typeof(TxTestDoc)], enableFileReference: true);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new StorageModule().OnApplicationInitializationAsync(new ApplicationInitializationContext(provider)));

        Assert.Contains(nameof(LongKeyedProduct), error.Message);
        Assert.DoesNotContain(nameof(TxTestDoc), error.Message);
    }

    [Fact]
    public async Task StartingTheModule_WithOnlyGuidKeyedFileFields_Succeeds()
    {
        await using var provider = ModuleProvider([typeof(TxTestDoc), typeof(LongKeyedLookup)], enableFileReference: true);

        await new StorageModule().OnApplicationInitializationAsync(new ApplicationInitializationContext(provider));
    }

    /// <summary>引用追踪关闭时 <c>[FileField]</c> 本来就不起作用，不拿它拦启动。</summary>
    [Fact]
    public async Task StartingTheModule_WithReferenceTrackingOff_DoesNotCheck()
    {
        await using var provider = ModuleProvider([typeof(LongKeyedProduct)], enableFileReference: false);

        await new StorageModule().OnApplicationInitializationAsync(new ApplicationInitializationContext(provider));
    }

    private static ServiceProvider ModuleProvider(Type[] entityTypes, bool enableFileReference)
    {
        var entityManager = new Mock<IEntityManager>();
        entityManager.Setup(m => m.GetAllEntityTypes()).Returns(entityTypes);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(entityManager.Object);
        services.AddSingleton<IOptionsMonitor<StorageOptions>>(
            new StaticOptionsMonitor<StorageOptions>(new StorageOptions { EnableFileReference = enableFileReference }));
        return services.BuildServiceProvider();
    }
}
