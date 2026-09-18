namespace Tnzi.Storage.Tests.Integration;

/// <summary>
/// <see cref="IFileContentReader"/>：以系统身份按 id 读字节，不看当前用户；
/// 与它同组的 <see cref="IFileReadAccessProbe"/> 在同一条真实仓储上必须也跑得通。
/// </summary>
/// <remarks>
/// ★ 两条契约都随本模块注册、都被通知模块以可选注入消费（附件的 FileId 在创建时过探针、派发时经读取器取字节）。
/// 这里用真实 SQLite 仓储 + LocalStorage，不 mock 被测对象。
/// </remarks>
public class FileContentReaderTests : StorageIntegrationTestBase
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.AddScoped<IReadOnlyRepository<FileRecord, Guid>>(sp => sp.GetRequiredService<IRepository<FileRecord, Guid>>());
        services.AddScoped<IFileContentReader, FileContentReader>();
        services.AddScoped<IFileReadAccessProbe, FileReadAccessProbe>();
        services.AddScoped<IFileAccessAuthorizer>(_ => new TestFileAccessAuthorizer(canRead: false));
    }

    /// <summary>★★ 私密文件、当前用户读不到（授权器判否）：读取器照样给出字节 —— 它是系统身份，授权在别处。</summary>
    [Fact]
    public async Task OpenRead_ReturnsTheStoredBytes_RegardlessOfTheCallersAccess()
    {
        var record = await SaveAsync("statement.txt", "hello attachment");

        using var scope = ServiceProvider.CreateScope();
        var reader = scope.ServiceProvider.GetRequiredService<IFileContentReader>();
        await using var stream = await reader.OpenReadAsync(record.Id);

        Assert.NotNull(stream);
        using var text = new StreamReader(stream);
        Assert.Equal("hello attachment", await text.ReadToEndAsync());
    }

    /// <summary>不存在的 id 与空 id 都是 null，不抛。</summary>
    [Fact]
    public async Task OpenRead_ReturnsNull_ForAnUnknownOrEmptyId()
    {
        using var scope = ServiceProvider.CreateScope();
        var reader = scope.ServiceProvider.GetRequiredService<IFileContentReader>();

        Assert.Null(await reader.OpenReadAsync(Guid.NewGuid()));
        Assert.Null(await reader.OpenReadAsync(Guid.Empty));
    }

    /// <summary>记录在、物理对象没了：null 而不是异常（记录指向的对象被外部删掉是运维现实）。</summary>
    [Fact]
    public async Task OpenRead_ReturnsNull_WhenTheObjectIsMissingFromTheProvider()
    {
        var record = await SaveAsync("gone.txt", "bye");
        await Storage.DeleteAsync(record.Path!);

        using var scope = ServiceProvider.CreateScope();
        var reader = scope.ServiceProvider.GetRequiredService<IFileContentReader>();

        Assert.Null(await reader.OpenReadAsync(record.Id));
    }

    /// <summary>
    /// 探针在真实仓储上必须回答得出来。它把 <c>(fileId, cancellationToken)</c> 交给的是
    /// <c>FindAsync(params object[] keys)</c>，看着像两个键值对一个单键实体；EF 的 <c>FindAsync</c>
    /// 会把结尾的 <c>CancellationToken</c> 当令牌而不是键。这里在真实 SQLite 上钉住这一点：
    /// 通知附件的创建入口现在也走它，它一旦抛，每一条「有当前用户地引用一份文件」的路径都会 500。
    /// </summary>
    [Fact]
    public async Task Probe_AnswersOnARealRepository_WithoutThrowing()
    {
        var record = await SaveAsync("probe.txt", "x");

        using var scope = ServiceProvider.CreateScope();
        var probe = scope.ServiceProvider.GetRequiredService<IFileReadAccessProbe>();

        Assert.False(await probe.CanReadAsync(record.Id, CancellationToken.None), "授权器判否，探针却放行了");
        Assert.False(await probe.CanReadAsync(Guid.NewGuid(), CancellationToken.None));
    }

    private async Task<FileRecord> SaveAsync(string name, string content)
    {
        using var scope = ServiceProvider.CreateScope();
        var path = await Storage.UploadAsync(name, new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content)), "text/plain");
        var record = new FileRecord
        {
            FileName = name,
            OriginalName = name,
            Extension = ".txt",
            Path = path,
            ContentType = "text/plain",
            Size = content.Length,
            Provider = Storage.ProviderName
        };
        await scope.ServiceProvider.GetRequiredService<IRepository<FileRecord, Guid>>().InsertAsync(record);
        return record;
    }
}
