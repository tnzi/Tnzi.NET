using Tnzi.EFCore;

namespace Tnzi.Storage.Tests.Integration;

/// <summary>
/// 验证文件引用去重（T8）：数据库唯一索引兜底 + 应用层查重，避免双轨写入产生重复引用行。
/// </summary>
public class FileReferenceDedupTests : StorageIntegrationTestBase
{
    [Fact]
    public async Task UniqueIndex_RejectsDuplicateReferenceRows()
    {
        var file = await CreateFileWithZeroRefAsync();
        var entityId = Guid.NewGuid();

        DbContext.FileReferences.Add(new FileReference
        {
            FileId = file.Id, EntityType = "BlogPost", EntityId = entityId, FieldName = "Cover", IsTemporary = false
        });
        DbContext.FileReferences.Add(new FileReference
        {
            FileId = file.Id, EntityType = "BlogPost", EntityId = entityId, FieldName = "Cover", IsTemporary = false
        });

        // 复合唯一索引 (FileId, EntityType, EntityId, FieldName) 应拒绝第二条相同 key 的引用。
        await Assert.ThrowsAnyAsync<Exception>(() => DbContext.SaveChangesAsync());
    }

    private async Task<FileRecord> CreateFileWithZeroRefAsync()
    {
        var record = new FileRecord
        {
            Id = Guid.NewGuid(),
            FileName = "x.txt",
            OriginalName = "x.txt",
            Extension = ".txt",
            Size = 3,
            Path = "x",
            Md5Hash = "abc",
            Provider = "Local",
            ContentType = "text/plain",
            ReferenceCount = 0
        };
        DbContext.FileRecords.Add(record);
        await DbContext.SaveChangesAsync();
        return record;
    }
}
