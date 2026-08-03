namespace Tnzi.Storage.Tests.Integration;

/// <summary>
/// 上传时显式表达"这个文件是公开的"，以及它与 MD5 去重的交界。
/// </summary>
public class PublicUploadTests : StorageIntegrationTestBase
{
    [Fact]
    public async Task SaveAsync_WithIsPublic_StoresAPublicRecord()
    {
        var service = CreateStorageService();

        var result = await service.SaveAsync("avatar.png", new MemoryStream("avatar-bytes"u8.ToArray()), isTemporary: true, isPublic: true);

        Assert.True(result.Succeeded);
        Assert.True(result.Data!.IsPublic);
    }

    [Fact]
    public async Task SaveAsync_DefaultsToPrivate()
    {
        var service = CreateStorageService();

        var result = await service.SaveAsync("contract.txt", new MemoryStream("secret"u8.ToArray()));

        Assert.True(result.Succeeded);
        Assert.False(result.Data!.IsPublic);
    }

    [Fact]
    public async Task SaveFromBytesAsync_CanRequestPublic()
    {
        var service = CreateStorageService();

        var result = await service.SaveFromBytesAsync("logo.png", "logo-bytes"u8.ToArray(), isPublic: true);

        Assert.True(result.Succeeded);
        Assert.True(result.Data!.IsPublic);
    }

    [Fact]
    public async Task PublicUpload_DoesNotPromoteAnExistingPrivateFileWithTheSameBytes()
    {
        // 内容相同不等于有权把**别人的**私密记录改成人人可读。
        // 宁可多存一份，也不做这次可见性提权。
        var service = CreateStorageService();
        var bytes = "same-bytes-for-both"u8.ToArray();

        var priv = await service.SaveAsync("secret.txt", new MemoryStream(bytes));
        Assert.True(priv.Succeeded);
        Assert.False(priv.Data!.IsPublic);

        var pub = await service.SaveAsync("avatar.txt", new MemoryStream(bytes), isTemporary: false, isPublic: true);

        Assert.True(pub.Succeeded);
        Assert.NotEqual(priv.Data.Id, pub.Data!.Id);
        Assert.True(pub.Data.IsPublic);
        // 原本那条私密记录纹丝不动。
        Assert.False((await DbContext.FileRecords.FindAsync(priv.Data.Id))!.IsPublic);
    }

    [Fact]
    public async Task PublicUpload_StillReusesAnExistingPublicFile()
    {
        // 命中的记录已经是公开的 → 去重照常生效，不做无谓的重复存储。
        var service = CreateStorageService();
        var bytes = "shared-public-bytes"u8.ToArray();

        var first = await service.SaveAsync("a.png", new MemoryStream(bytes), isTemporary: false, isPublic: true);
        DbContext.ChangeTracker.Clear(); // 第二次上传属于另一个请求
        var second = await service.SaveAsync("b.png", new MemoryStream(bytes), isTemporary: false, isPublic: true);

        Assert.True(second.Succeeded);
        Assert.Equal(first.Data!.Id, second.Data!.Id);
    }

    [Fact]
    public async Task PrivateUpload_StillReusesAnExistingPublicFile()
    {
        // 反方向不构成提权（公开 → 私密的诉求不会降级已公开的记录，
        // 也不该为此另存一份），保持既有去重行为。
        var service = CreateStorageService();
        var bytes = "public-then-private"u8.ToArray();

        var first = await service.SaveAsync("a.png", new MemoryStream(bytes), isTemporary: false, isPublic: true);
        DbContext.ChangeTracker.Clear(); // 第二次上传属于另一个请求
        var second = await service.SaveAsync("b.png", new MemoryStream(bytes));

        Assert.True(second.Succeeded);
        Assert.Equal(first.Data!.Id, second.Data!.Id);
    }
}
