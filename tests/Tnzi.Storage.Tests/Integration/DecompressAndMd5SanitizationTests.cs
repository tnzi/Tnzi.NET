using System.IO.Compression;
using Tnzi.Security;

namespace Tnzi.Storage.Tests.Integration;

/// <summary>
/// 净化管线必须覆盖<b>每一条</b>把字节交给 provider 的写路径：解压出来的条目与 MD5 直存也不例外。
/// </summary>
/// <remarks>
/// <para>
/// <c>UploadGuard</c> 的契约写着「把字节交给存储提供者的路径必须过同一份闸门」，而 2026-08-23 落到解压那条路上的
/// 只有扩展名白名单：注册了病毒扫描器 / 类型核对的部署，先上传一个内容与扩展名一致的正常 <c>payload.zip</c>
/// （净化器只看到 zip 头，放行），再 <c>POST /files/{id}/decompress</c>，包里每一条都被写进对象存储并各得一条可下载的
/// 记录 —— 一次都没经过净化器；<c>AllowedExtensions</c> 默认为空时连扩展名闸门也在空转。日志、接口返回、
/// 管理端列表全部正常，与「净化器工作正常」在观测上不可区分。<c>GetOrCreateByMd5Async</c> 是公开契约上的同款。
/// </para>
/// </remarks>
public class DecompressAndMd5SanitizationTests : StorageIntegrationTestBase
{
    private static StorageOptions Options() => new()
    {
        MaxFileSize = 50 * 1024 * 1024,
        // 白名单为空：默认配置。解压路径唯一剩下的闸门就是净化管线。
        AllowedExtensions = [],
        AutoGenerateThumbnail = false
    };

    private static byte[] Zip(params (string Name, byte[] Bytes)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, bytes) in entries)
            {
                var entry = archive.CreateEntry(name);
                using var stream = entry.Open();
                stream.Write(bytes);
            }
        }

        return buffer.ToArray();
    }

    // ── 解压 ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task DecompressAsync_RunsSanitizerPerEntry_SkipsRejectedEntries()
    {
        var sanitizer = new NameBasedSanitizer { RejectWhenNameContains = "evil" };
        var service = CreateStorageService(Options(), sanitizers: [sanitizer]);
        var zip = await CreateStoredFileAsync("bundle.zip", Zip(("invoice.txt", "fine"u8.ToArray()), ("evil.png", "<html>"u8.ToArray())));

        var result = await service.DecompressAsync(zip.Id);

        Assert.True(result.Succeeded, result.Message);
        var extracted = result.Data!.ToList();
        var only = Assert.Single(extracted);
        Assert.Equal("invoice.txt", only.OriginalName);
        // 两条都过了净化器：被拒的那条没有落到 provider，也没有记录。
        Assert.Equal(new[] { "invoice.txt", "evil.png" }, sanitizer.SeenNames);
        Assert.DoesNotContain(await DbContext.FileRecords.ToListAsync(), r => r.OriginalName == "evil.png");
    }

    [Fact]
    public async Task DecompressAsync_SanitizerReplacement_IsWhatGetsStoredAndHashed()
    {
        var cleaned = "CLEANED"u8.ToArray();
        var sanitizer = new NameBasedSanitizer { ReplaceWith = cleaned };
        var service = CreateStorageService(Options(), sanitizers: [sanitizer]);
        var zip = await CreateStoredFileAsync("bundle.zip", Zip(("photo.jpg", "ORIGINAL-WITH-GPS"u8.ToArray())));

        var result = await service.DecompressAsync(zip.Id);

        Assert.True(result.Succeeded, result.Message);
        var record = Assert.Single(result.Data!);
        Assert.Equal(cleaned.LongLength, record.Size);
        Assert.Equal(await HashHelper.GetMd5Async(new MemoryStream(cleaned)), record.Md5Hash);
        await using var stored = await Storage.DownloadAsync(record.Path!);
        using var buffer = new MemoryStream();
        await stored.CopyToAsync(buffer);
        Assert.Equal(cleaned, buffer.ToArray());
    }

    // ── MD5 直存 ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetOrCreateByMd5Async_RejectingSanitizer_StoresNothing()
    {
        var sanitizer = new NameBasedSanitizer { RejectWhenNameContains = "payload" };
        var service = CreateStorageService(Options(), sanitizers: [sanitizer]);
        var bytes = "bad"u8.ToArray();
        var md5 = await HashHelper.GetMd5Async(new MemoryStream(bytes));

        var result = await service.GetOrCreateByMd5Async(md5, "payload.txt", new MemoryStream(bytes));

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
        Assert.Empty(await DbContext.FileRecords.ToListAsync());
    }

    [Fact]
    public async Task GetOrCreateByMd5Async_ReplacingSanitizer_StoresAndHashesTheSanitizedBytes()
    {
        // 调用方给的是原始字节的哈希；净化器改写了内容之后，那个哈希描述的是一份永远不会落库的东西 ——
        // 去重与落库都按净化后的哈希走，而不是把它当成「哈希不符」拒掉（那会让每一个剥元数据的部署都存不进图片）。
        var original = "ORIGINAL-WITH-GPS"u8.ToArray();
        var cleaned = "CLEANED"u8.ToArray();
        var sanitizer = new NameBasedSanitizer { ReplaceWith = cleaned };
        var service = CreateStorageService(Options(), sanitizers: [sanitizer]);
        var originalMd5 = await HashHelper.GetMd5Async(new MemoryStream(original));

        var result = await service.GetOrCreateByMd5Async(originalMd5, "photo.jpg", new MemoryStream(original));

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(await HashHelper.GetMd5Async(new MemoryStream(cleaned)), result.Data!.Md5Hash);
        Assert.Equal(cleaned.LongLength, result.Data.Size);
        await using var stored = await Storage.DownloadAsync(result.Data.Path!);
        using var buffer = new MemoryStream();
        await stored.CopyToAsync(buffer);
        Assert.Equal(cleaned, buffer.ToArray());
    }

    [Fact]
    public async Task GetOrCreateByMd5Async_UnchangedContent_StillEnforcesTheSuppliedHash()
    {
        // 反向护栏：净化器没改内容时，调用方哈希与实际字节不符仍是 400。
        var sanitizer = new NameBasedSanitizer();
        var service = CreateStorageService(Options(), sanitizers: [sanitizer]);

        var result = await service.GetOrCreateByMd5Async("00000000000000000000000000000000", "note.txt", new MemoryStream("x"u8.ToArray()));

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
        Assert.Contains("mismatch", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class NameBasedSanitizer : IUploadSanitizer
    {
        public string? RejectWhenNameContains { get; init; }
        public byte[]? ReplaceWith { get; init; }
        public List<string> SeenNames { get; } = [];

        public int Order => 100;

        public Task<UploadSanitizationResult> SanitizeAsync(UploadSanitizationContext context, CancellationToken cancellationToken = default)
        {
            SeenNames.Add(context.FileName);

            if (RejectWhenNameContains != null && context.FileName.Contains(RejectWhenNameContains, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(UploadSanitizationResult.Reject("Simulated malware detected."));
            }

            return Task.FromResult(ReplaceWith != null
                ? UploadSanitizationResult.Replaced(new MemoryStream(ReplaceWith))
                : UploadSanitizationResult.Unchanged());
        }
    }
}
