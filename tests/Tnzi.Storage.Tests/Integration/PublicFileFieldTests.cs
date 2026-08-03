
namespace Tnzi.Storage.Tests.Integration;

/// <summary>
/// 覆盖"有意公开的文件"这条链路：字段声明 → 写引用时自动公开 → 历史数据回填。
///
/// 这些用例守的是一个具体的线上故障：1aa59e10 给 <c>FileRecord</c> 加了 <c>IsPublic</c>
/// 并把读取收紧到"公开 / 归属 / 权限码"，但当时**没有任何代码路径能把它置 true**，
/// 于是所有以匿名 <c>&lt;img src="/api/files/{id}/download"&gt;</c> 消费的头像全变 404。
/// </summary>
public class PublicFileFieldTests : StorageIntegrationTestBase
{
    private const string AvatarEntity = "UserDetail";
    private const string AvatarField = "AvatarId";

    [Fact]
    public async Task ReferenceFromPublicField_MarksTheFilePublic()
    {
        var file = await CreateFileAsync(isPublic: false);
        var processor = CreateReferenceProcessor();

        await processor.ProcessChangesAsync([PublicChange(file.Id)]);
        await DbContext.SaveChangesAsync();

        var reloaded = await DbContext.FileRecords.FindAsync(file.Id);
        Assert.True(reloaded!.IsPublic);
    }

    [Fact]
    public async Task ReferenceFromOrdinaryField_LeavesTheFilePrivate()
    {
        // 合同 / 支票 / HR 文件挂的是普通 [FileField]，绝不能被顺带公开。
        var file = await CreateFileAsync(isPublic: false);
        var processor = CreateReferenceProcessor();

        await processor.ProcessChangesAsync([PublicChange(file.Id, isPublicFile: false, entityType: "Invoice", fieldName: "PdfFileId")]);
        await DbContext.SaveChangesAsync();

        var reloaded = await DbContext.FileRecords.FindAsync(file.Id);
        Assert.False(reloaded!.IsPublic);
    }

    [Fact]
    public async Task ReferenceFromPublicField_RepairsAnAlreadyReferencedFile()
    {
        // 引用行已经在了（历史数据），重存一次实体应当把公开标记补上，
        // 而不是因为"引用已存在"就整段跳过。引用计数不能因此重复递增。
        var file = await CreateFileAsync(isPublic: false);
        var entityId = Guid.NewGuid();
        DbContext.FileReferences.Add(new FileReference
        {
            FileId = file.Id,
            EntityType = AvatarEntity,
            EntityId = entityId,
            FieldName = AvatarField,
            IsTemporary = false
        });
        await DbContext.SaveChangesAsync();
        var countBefore = file.ReferenceCount;

        var processor = CreateReferenceProcessor();
        await processor.ProcessChangesAsync([PublicChange(file.Id, entityId: entityId)]);
        await DbContext.SaveChangesAsync();

        var reloaded = await DbContext.FileRecords.FindAsync(file.Id);
        Assert.True(reloaded!.IsPublic);
        Assert.Equal(countBefore, reloaded.ReferenceCount);
    }

    [Fact]
    public async Task RemovingAReference_DoesNotTakeThePublicFlagBack()
    {
        // 只升不降：同一个文件可能仍被别处公开引用，取消引用不等于要收回公开。
        var file = await CreateFileAsync(isPublic: true);
        var entityId = Guid.NewGuid();
        DbContext.FileReferences.Add(new FileReference
        {
            FileId = file.Id,
            EntityType = AvatarEntity,
            EntityId = entityId,
            FieldName = AvatarField,
            IsTemporary = false
        });
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var processor = CreateReferenceProcessor();
        await processor.ProcessChangesAsync([new FileReferenceChange
        {
            ChangeType = FileReferenceChangeType.Delete,
            EntityType = AvatarEntity,
            EntityId = entityId.ToString(),
            FieldName = AvatarField,
            FileId = file.Id
        }]);
        await DbContext.SaveChangesAsync();

        var reloaded = await DbContext.FileRecords.FindAsync(file.Id);
        Assert.True(reloaded!.IsPublic);
    }

    [Fact]
    public async Task Backfill_MarksHistoricalFilesReferencedByPublicFields()
    {
        var avatar = await CreateFileAsync(isPublic: false);
        var contract = await CreateFileAsync(isPublic: false);

        DbContext.FileReferences.Add(new FileReference
        {
            FileId = avatar.Id, EntityType = AvatarEntity, EntityId = Guid.NewGuid(), FieldName = AvatarField, IsTemporary = false
        });
        DbContext.FileReferences.Add(new FileReference
        {
            FileId = contract.Id, EntityType = "Invoice", EntityId = Guid.NewGuid(), FieldName = "PdfFileId", IsTemporary = false
        });
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var service = CreateStorageService(
            publicFieldResolver: TestPublicFileFieldResolver.With((AvatarEntity, AvatarField)));

        var result = await service.SyncPublicFlagsFromReferencesAsync();
        await DbContext.SaveChangesAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.Data);
        Assert.True((await DbContext.FileRecords.FindAsync(avatar.Id))!.IsPublic);
        Assert.False((await DbContext.FileRecords.FindAsync(contract.Id))!.IsPublic);
    }

    [Fact]
    public async Task Backfill_IgnoresAFieldNameThatIsOnlyPublicOnAnotherEntity()
    {
        // 两个平行数组交给 SQL 会命中 (EntityType, FieldName) 的笛卡尔积，
        // 回读后必须按真实声明精确过滤，否则会误公开同名字段。
        var stray = await CreateFileAsync(isPublic: false);
        DbContext.FileReferences.Add(new FileReference
        {
            FileId = stray.Id, EntityType = "Invoice", EntityId = Guid.NewGuid(), FieldName = AvatarField, IsTemporary = false
        });
        await DbContext.SaveChangesAsync();

        var service = CreateStorageService(publicFieldResolver: TestPublicFileFieldResolver.With(
            (AvatarEntity, AvatarField),
            ("Invoice", "PdfFileId")));

        var result = await service.SyncPublicFlagsFromReferencesAsync();
        await DbContext.SaveChangesAsync();

        Assert.Equal(0, result.Data);
        Assert.False((await DbContext.FileRecords.FindAsync(stray.Id))!.IsPublic);
    }

    [Fact]
    public async Task Backfill_IsIdempotentAndNeverDemotes()
    {
        var avatar = await CreateFileAsync(isPublic: true);
        DbContext.FileReferences.Add(new FileReference
        {
            FileId = avatar.Id, EntityType = AvatarEntity, EntityId = Guid.NewGuid(), FieldName = AvatarField, IsTemporary = false
        });
        await DbContext.SaveChangesAsync();

        var service = CreateStorageService(
            publicFieldResolver: TestPublicFileFieldResolver.With((AvatarEntity, AvatarField)));

        // 已经公开 → 本次没有改动可报；文件当然仍是公开的。
        var result = await service.SyncPublicFlagsFromReferencesAsync();
        Assert.Equal(0, result.Data);
        Assert.True((await DbContext.FileRecords.FindAsync(avatar.Id))!.IsPublic);
    }

    [Fact]
    public async Task Backfill_WithNoDeclarations_DoesNothing()
    {
        var file = await CreateFileAsync(isPublic: false);
        DbContext.FileReferences.Add(new FileReference
        {
            FileId = file.Id, EntityType = AvatarEntity, EntityId = Guid.NewGuid(), FieldName = AvatarField, IsTemporary = false
        });
        await DbContext.SaveChangesAsync();

        var service = CreateStorageService(publicFieldResolver: TestPublicFileFieldResolver.Empty());

        var result = await service.SyncPublicFlagsFromReferencesAsync();
        Assert.Equal(0, result.Data);
        Assert.False((await DbContext.FileRecords.FindAsync(file.Id))!.IsPublic);
    }

    [Fact]
    public async Task SetFileVisibility_FlipsTheFlagBothWays()
    {
        var file = await CreateFileAsync(isPublic: false);
        var service = CreateStorageService();

        var made = await service.SetFileVisibilityAsync(file.Id, isPublic: true);
        Assert.True(made.Succeeded);
        Assert.True((await DbContext.FileRecords.FindAsync(file.Id))!.IsPublic);

        var revoked = await service.SetFileVisibilityAsync(file.Id, isPublic: false);
        Assert.True(revoked.Succeeded);
        Assert.False((await DbContext.FileRecords.FindAsync(file.Id))!.IsPublic);
    }

    [Fact]
    public async Task SetFileVisibility_OnAMissingFile_Is404()
    {
        var service = CreateStorageService();

        var result = await service.SetFileVisibilityAsync(Guid.NewGuid(), isPublic: true);

        Assert.False(result.Succeeded);
        Assert.Equal(404, result.Code);
    }

    private static FileReferenceChange PublicChange(
        Guid fileId,
        bool isPublicFile = true,
        string entityType = AvatarEntity,
        string fieldName = AvatarField,
        Guid? entityId = null) => new()
        {
            ChangeType = FileReferenceChangeType.Create,
            EntityType = entityType,
            EntityId = (entityId ?? Guid.NewGuid()).ToString(),
            FieldName = fieldName,
            FileId = fileId,
            IsPublicFile = isPublicFile
        };

    private async Task<FileRecord> CreateFileAsync(bool isPublic)
    {
        var record = new FileRecord
        {
            Id = Guid.NewGuid(),
            FileName = $"{Guid.NewGuid():N}.txt",
            OriginalName = "avatar.txt",
            Extension = ".txt",
            Size = 3,
            Path = "x",
            Provider = "Local",
            ContentType = "text/plain",
            IsPublic = isPublic,
            ReferenceCount = 1
        };
        DbContext.FileRecords.Add(record);
        await DbContext.SaveChangesAsync();
        return record;
    }
}
