using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Tnzi.Storage.Helpers;

namespace Tnzi.Storage.Tests.Integration;

/// <summary>
/// PDF 首页缩略图：可选包 <c>Tnzi.Documents</c> 加载了、原生库装得上时，存进来的 PDF 要有一张整页等比的缩略图；
/// 其余每一种缺席 / 失效形态都要与此前逐字相同 —— 没有图、上传照常成功、不抛、不记 ERROR。
/// </summary>
/// <remarks>
/// 本测试项目**不引用** <c>Tnzi.Documents</c>：光栅化器由 <see cref="FakePdfRasterizer"/> 扮演，
/// 生成器、解码闸门、缩放、编码、写回 provider、落库这一段全是真实代码。
/// </remarks>
public class PdfThumbnailTests : StorageIntegrationTestBase
{
    /// <summary>一份足以通过扩展名闸门的「PDF」；光栅化器是替身，字节内容无关紧要。</summary>
    private static readonly byte[] PdfBytes = "%PDF-1.4 fake"u8.ToArray();

    private static StorageOptions ThumbnailOptions(Action<StorageOptions>? configure = null)
    {
        var options = new StorageOptions
        {
            MaxFileSize = 50 * 1024 * 1024,
            AllowedExtensions = [".pdf", ".png", ".txt"],
            AutoGenerateThumbnail = true,
        };
        configure?.Invoke(options);
        return options;
    }

    private async Task<Image<Rgba32>> ReadThumbnailAsync(FileStorageService service, Guid id)
    {
        var stream = await service.GetThumbnailAsync(id);
        Assert.True(stream.Succeeded, stream.Message);
        using var s = stream.Data!;
        return await Image.LoadAsync<Rgba32>(s);
    }

    // ---------- 主路径：包在、原生库在 ----------

    [Fact]
    public async Task SaveAsync_Pdf_WithRasterizer_StoresAWholePageThumbnail()
    {
        var rasterizer = new FakePdfRasterizer { PageWidth = 1700, PageHeight = 700 };
        var service = CreateStorageService(options: ThumbnailOptions(), pdfRasterizer: rasterizer);

        var saved = await service.SaveAsync("cheque.pdf", new MemoryStream(PdfBytes));

        Assert.True(saved.Succeeded, saved.Message);
        var record = saved.Data!;
        Assert.False(string.IsNullOrEmpty(record.ThumbnailPath));
        Assert.True(await Storage.ExistsAsync(record.ThumbnailPath!));

        // 第 0 页、彩色、按配置的 dpi
        Assert.Equal(1, rasterizer.RenderCalls);
        Assert.Equal(0, rasterizer.LastPageIndex);
        Assert.False(rasterizer.LastRequest!.Grayscale);
        Assert.Equal(100, rasterizer.LastRequest.Dpi);

        // 整页等比缩进 600×800 的盒子：横向的 1700×700 → 600×247，两边都还在
        // （ResizeToFit 用 (int) 截断，浮点误差可能少一个像素；守的是形状不是那一个像素）
        using var thumbnail = await ReadThumbnailAsync(service, record.Id);
        Assert.InRange(thumbnail.Width, 599, 600);
        Assert.InRange(thumbnail.Height, 246, 248);
        // 左上角那块深色仍在左上角（没有被中心裁剪吃掉）
        Assert.True(thumbnail[2, 2].R < 80, "top-left marker should survive fit-in-box scaling");
        Assert.True(thumbnail[thumbnail.Width - 3, thumbnail.Height - 3].R > 200, "bottom-right should still be page white");
    }

    [Fact]
    public async Task SaveAsync_Pdf_PortraitPage_FitsTheBoxWithoutCropping()
    {
        // US Letter @ 100 dpi
        var rasterizer = new FakePdfRasterizer { PageWidth = 850, PageHeight = 1100 };
        var service = CreateStorageService(options: ThumbnailOptions(), pdfRasterizer: rasterizer);

        var saved = await service.SaveAsync("letter.pdf", new MemoryStream(PdfBytes));

        using var thumbnail = await ReadThumbnailAsync(service, saved.Data!.Id);
        // 850×1100 缩进 600×800：受宽度约束 → 600×776
        Assert.InRange(thumbnail.Width, 599, 600);
        Assert.InRange(thumbnail.Height, 774, 777);
    }

    [Fact]
    public async Task SaveAsync_Pdf_ThumbnailIsJpeg_ServedByGetThumbnail()
    {
        var service = CreateStorageService(options: ThumbnailOptions(), pdfRasterizer: new FakePdfRasterizer());
        var saved = await service.SaveAsync("a.pdf", new MemoryStream(PdfBytes));

        var stream = await service.GetThumbnailAsync(saved.Data!.Id);

        Assert.True(stream.Succeeded);
        using var s = stream.Data!;
        var header = new byte[3];
        Assert.Equal(3, await s.ReadAsync(header));
        // 控制器把 /thumbnail 恒标成 image/jpeg；PDF 的缩略图必须真的是 JPEG
        Assert.Equal(new byte[] { 0xFF, 0xD8, 0xFF }, header);
    }

    [Fact]
    public async Task SaveAsync_Pdf_BoxAndDpiComeFromOptions()
    {
        var rasterizer = new FakePdfRasterizer { PageWidth = 1700, PageHeight = 700 };
        var options = ThumbnailOptions(o =>
        {
            o.PdfThumbnail.MaxWidth = 300;
            o.PdfThumbnail.MaxHeight = 300;
            o.PdfThumbnail.Dpi = 72;
        });
        var service = CreateStorageService(options: options, pdfRasterizer: rasterizer);

        var saved = await service.SaveAsync("a.pdf", new MemoryStream(PdfBytes));

        Assert.Equal(72, rasterizer.LastRequest!.Dpi);
        using var thumbnail = await ReadThumbnailAsync(service, saved.Data!.Id);
        Assert.InRange(thumbnail.Width, 299, 300);
        Assert.InRange(thumbnail.Height, 122, 124);
    }

    // ---------- 其余写路径 ----------

    [Fact]
    public async Task CopyAsync_Pdf_GeneratesAThumbnailForTheCopy()
    {
        var rasterizer = new FakePdfRasterizer();
        var service = CreateStorageService(options: ThumbnailOptions(), pdfRasterizer: rasterizer);
        var source = await CreateStoredFileAsync("a.pdf", PdfBytes);
        Assert.Null(source.ThumbnailPath);

        var copied = await service.CopyAsync(source.Id);

        Assert.True(copied.Succeeded, copied.Message);
        Assert.False(string.IsNullOrEmpty(copied.Data!.ThumbnailPath));
        Assert.Equal(1, rasterizer.RenderCalls);
    }

    [Fact]
    public async Task GetOrCreateByMd5Async_Pdf_GeneratesAThumbnail()
    {
        var rasterizer = new FakePdfRasterizer();
        var service = CreateStorageService(options: ThumbnailOptions(), pdfRasterizer: rasterizer);
        var md5 = Convert.ToHexString(MD5.HashData(PdfBytes)).ToLowerInvariant();

        var saved = await service.GetOrCreateByMd5Async(md5, "a.pdf", new MemoryStream(PdfBytes));

        Assert.True(saved.Succeeded, saved.Message);
        Assert.False(string.IsNullOrEmpty(saved.Data!.ThumbnailPath));
        Assert.Equal(1, rasterizer.RenderCalls);
    }

    [Fact]
    public async Task SaveAsync_Pdf_ReuploadWhenObjectMissing_GeneratesTheThumbnailItNeverHad()
    {
        // 第一次：没加载可选包，存进来的 PDF 没有缩略图
        var without = CreateStorageService(options: ThumbnailOptions());
        var first = await without.SaveAsync("a.pdf", new MemoryStream(PdfBytes));
        Assert.Null(first.Data!.ThumbnailPath);

        // 对象丢了；之后加载了可选包再传同样的字节 → 走「记录在、对象不在」的重传路径
        await Storage.DeleteAsync(first.Data.Path!);
        DbContext.ChangeTracker.Clear();
        var rasterizer = new FakePdfRasterizer();
        var with = CreateStorageService(options: ThumbnailOptions(), pdfRasterizer: rasterizer);

        var second = await with.SaveAsync("a.pdf", new MemoryStream(PdfBytes));

        Assert.True(second.Succeeded, second.Message);
        Assert.Equal(first.Data.Id, second.Data!.Id);
        Assert.False(string.IsNullOrEmpty(second.Data.ThumbnailPath));
        Assert.Equal(1, rasterizer.RenderCalls);
    }

    // ---------- 缺席 / 失效：每一种都要与此前逐字相同 ----------

    [Fact]
    public async Task SaveAsync_Pdf_WithoutThePackage_HasNoThumbnail_AndSucceeds()
    {
        var service = CreateStorageService(options: ThumbnailOptions());

        var saved = await service.SaveAsync("a.pdf", new MemoryStream(PdfBytes));

        Assert.True(saved.Succeeded, saved.Message);
        Assert.Null(saved.Data!.ThumbnailPath);
        Assert.True(await Storage.ExistsAsync(saved.Data.Path!));
        Assert.Equal(404, (await service.GetThumbnailAsync(saved.Data.Id)).Code);
    }

    [Fact]
    public async Task SaveAsync_Pdf_PackageLoadedButNativeLibraryMissing_NeverAsksTheRasterizer()
    {
        var rasterizer = new FakePdfRasterizer { Available = false };
        var service = CreateStorageService(options: ThumbnailOptions(), pdfRasterizer: rasterizer);

        var saved = await service.SaveAsync("a.pdf", new MemoryStream(PdfBytes));

        Assert.True(saved.Succeeded, saved.Message);
        Assert.Null(saved.Data!.ThumbnailPath);
        // 可预期的「本机画不了」不是一次失败：连试都不试，自然也没有日志
        Assert.Equal(0, rasterizer.RenderCalls);
        Assert.Equal(0, rasterizer.PageCountCalls);
    }

    [Fact]
    public async Task SaveAsync_Pdf_SwitchedOff_KeepsBitmapsButNotPdf()
    {
        var rasterizer = new FakePdfRasterizer();
        var service = CreateStorageService(
            options: ThumbnailOptions(o => o.PdfThumbnail.Enabled = false),
            pdfRasterizer: rasterizer);

        var pdf = await service.SaveAsync("a.pdf", new MemoryStream(PdfBytes));
        var png = await service.SaveAsync("a.png", new MemoryStream(TinyPng(10, 10)));

        Assert.Null(pdf.Data!.ThumbnailPath);
        Assert.Equal(0, rasterizer.RenderCalls);
        Assert.False(string.IsNullOrEmpty(png.Data!.ThumbnailPath));
    }

    [Fact]
    public async Task SaveAsync_Pdf_RenderThrows_UploadStillSucceeds()
    {
        var rasterizer = new FakePdfRasterizer { ThrowOnRender = new InvalidOperationException("password protected") };
        var service = CreateStorageService(options: ThumbnailOptions(), pdfRasterizer: rasterizer);

        var saved = await service.SaveAsync("locked.pdf", new MemoryStream(PdfBytes));

        Assert.True(saved.Succeeded, saved.Message);
        Assert.Null(saved.Data!.ThumbnailPath);
        Assert.True(await Storage.ExistsAsync(saved.Data.Path!));
        var download = await service.GetAsync(saved.Data.Id);
        Assert.True(download.Succeeded);
        download.Data!.Dispose();
    }

    [Fact]
    public async Task SaveAsync_Pdf_PageCountThrows_UploadStillSucceeds()
    {
        var rasterizer = new FakePdfRasterizer { ThrowOnPageCount = new InvalidOperationException("not a pdf") };
        var service = CreateStorageService(options: ThumbnailOptions(), pdfRasterizer: rasterizer);

        var saved = await service.SaveAsync("corrupt.pdf", new MemoryStream(PdfBytes));

        Assert.True(saved.Succeeded, saved.Message);
        Assert.Null(saved.Data!.ThumbnailPath);
        Assert.Equal(0, rasterizer.RenderCalls);
    }

    [Fact]
    public async Task SaveAsync_Pdf_ZeroPages_HasNoThumbnail_AndNeverRenders()
    {
        var rasterizer = new FakePdfRasterizer { PageCount = 0 };
        var service = CreateStorageService(options: ThumbnailOptions(), pdfRasterizer: rasterizer);

        var saved = await service.SaveAsync("empty.pdf", new MemoryStream(PdfBytes));

        Assert.True(saved.Succeeded, saved.Message);
        Assert.Null(saved.Data!.ThumbnailPath);
        Assert.Equal(0, rasterizer.RenderCalls);
    }

    [Fact]
    public async Task SaveAsync_Pdf_OverMaxSourceBytes_IsNotDrawn_AndNotEvenRead()
    {
        var rasterizer = new FakePdfRasterizer();
        var recorder = new RecordingFileStorage(Storage);
        var service = CreateStorageService(
            recorder,
            options: ThumbnailOptions(o => o.PdfThumbnail.MaxSourceBytes = PdfBytes.Length - 1),
            pdfRasterizer: rasterizer);

        var saved = await service.SaveAsync("huge.pdf", new MemoryStream(PdfBytes));

        Assert.True(saved.Succeeded, saved.Message);
        Assert.Null(saved.Data!.ThumbnailPath);
        Assert.Equal(0, rasterizer.PageCountCalls);
        Assert.Equal(0, rasterizer.RenderCalls);
        // 记录里的 Size 已经说明它太大：连回读那一趟都省掉（几百 MB 的扫描册子不该为一张不会画的图被读一遍）
        Assert.Empty(recorder.DownloadedPaths);
    }

    [Fact]
    public async Task SaveAsync_Pdf_SizeUnknownButObjectOverLimit_IsCaughtWhileReading()
    {
        // Size 为 0 = 「不知道」（provider 量不出来）：预检放行，读的时候按实际字节再拦一次
        var rasterizer = new FakePdfRasterizer();
        var generator = new FileThumbnailGenerator(
            Storage,
            new StaticOptionsMonitor<StorageOptions>(ThumbnailOptions(o => o.PdfThumbnail.MaxSourceBytes = PdfBytes.Length - 1)),
            pdfRasterizer: rasterizer);
        var originalPath = await Storage.UploadAsync("big.pdf", new MemoryStream(PdfBytes), "application/pdf");

        var path = await generator.GenerateAsync(originalPath, "big.pdf", ".pdf", size: 0);

        Assert.Null(path);
        Assert.Equal(0, rasterizer.PageCountCalls);
    }

    [Fact]
    public async Task SaveAsync_Pdf_MaxSourceBytesZero_MeansNoLimit()
    {
        var rasterizer = new FakePdfRasterizer();
        var service = CreateStorageService(
            options: ThumbnailOptions(o => o.PdfThumbnail.MaxSourceBytes = 0),
            pdfRasterizer: rasterizer);

        var saved = await service.SaveAsync("a.pdf", new MemoryStream(PdfBytes));

        Assert.False(string.IsNullOrEmpty(saved.Data!.ThumbnailPath));
        Assert.Equal(PdfBytes.Length, rasterizer.LastSourceLength);
    }

    [Fact]
    public async Task SaveAsync_Pdf_AutoGenerateOff_DrawsNothing()
    {
        var rasterizer = new FakePdfRasterizer();
        var service = CreateStorageService(
            options: ThumbnailOptions(o => o.AutoGenerateThumbnail = false),
            pdfRasterizer: rasterizer);

        var saved = await service.SaveAsync("a.pdf", new MemoryStream(PdfBytes));

        Assert.Null(saved.Data!.ThumbnailPath);
        Assert.Equal(0, rasterizer.RenderCalls);
    }

    // ---------- 位图路径：形状不能变 ----------

    [Fact]
    public async Task SaveAsync_Bitmap_StillProducesTheSquareThumbnail()
    {
        // 位图消费方（头像、相册）靠的就是「缩略图是方的」；PDF 的整页形状不得漂到这里来
        var service = CreateStorageService(options: ThumbnailOptions(), pdfRasterizer: new FakePdfRasterizer());

        var saved = await service.SaveAsync("wide.png", new MemoryStream(TinyPng(400, 100)));

        Assert.False(string.IsNullOrEmpty(saved.Data!.ThumbnailPath));
        using var thumbnail = await ReadThumbnailAsync(service, saved.Data.Id);
        Assert.Equal(200, thumbnail.Width);
        Assert.Equal(200, thumbnail.Height);
    }

    // ---------- 回填 ----------

    [Fact]
    public async Task BackfillThumbnailsAsync_DrawsOnlyRecordsWithoutOne_AndIsIdempotent()
    {
        var rasterizer = new FakePdfRasterizer();
        var service = CreateStorageService(options: ThumbnailOptions(), pdfRasterizer: rasterizer);

        var a = await CreateStoredFileAsync("a.pdf", PdfBytes);
        var b = await CreateStoredFileAsync("b.pdf", PdfBytes);
        var already = await CreateStoredFileAsync("c.pdf", PdfBytes);
        already.ThumbnailPath = "thumb/existing";
        var text = await CreateStoredFileAsync("d.txt", "hello"u8.ToArray());
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var first = await service.BackfillThumbnailsAsync();

        Assert.True(first.Succeeded, first.Message);
        Assert.Equal(2, first.Data!.Scanned);
        Assert.Equal(2, first.Data.Generated);
        Assert.Equal(0, first.Data.Failed);
        Assert.Equal(0, first.Data.Remaining);
        Assert.Equal(2, rasterizer.RenderCalls);

        DbContext.ChangeTracker.Clear();
        var records = DbContext.FileRecords.ToDictionary(f => f.Id);
        Assert.False(string.IsNullOrEmpty(records[a.Id].ThumbnailPath));
        Assert.False(string.IsNullOrEmpty(records[b.Id].ThumbnailPath));
        Assert.Equal("thumb/existing", records[already.Id].ThumbnailPath);
        Assert.Null(records[text.Id].ThumbnailPath);

        // 第二次：没有候选了，也不再渲染
        var second = await service.BackfillThumbnailsAsync();
        Assert.Equal(0, second.Data!.Scanned);
        Assert.Equal(0, second.Data.Generated);
        Assert.Equal(2, rasterizer.RenderCalls);
    }

    [Fact]
    public async Task BackfillThumbnailsAsync_FileIds_LimitsTheScope()
    {
        var rasterizer = new FakePdfRasterizer();
        var service = CreateStorageService(options: ThumbnailOptions(), pdfRasterizer: rasterizer);
        var a = await CreateStoredFileAsync("a.pdf", PdfBytes);
        var b = await CreateStoredFileAsync("b.pdf", PdfBytes);
        DbContext.ChangeTracker.Clear();

        var result = await service.BackfillThumbnailsAsync([a.Id]);

        Assert.Equal(1, result.Data!.Generated);
        DbContext.ChangeTracker.Clear();
        Assert.False(string.IsNullOrEmpty(DbContext.FileRecords.Single(f => f.Id == a.Id).ThumbnailPath));
        Assert.Null(DbContext.FileRecords.Single(f => f.Id == b.Id).ThumbnailPath);
    }

    [Fact]
    public async Task BackfillThumbnailsAsync_MaxFiles_BatchesAndReportsRemaining()
    {
        var rasterizer = new FakePdfRasterizer();
        var service = CreateStorageService(options: ThumbnailOptions(), pdfRasterizer: rasterizer);
        for (var i = 0; i < 3; i++)
            await CreateStoredFileAsync($"{i}.pdf", PdfBytes);
        DbContext.ChangeTracker.Clear();

        var first = await service.BackfillThumbnailsAsync(maxFiles: 2);
        Assert.Equal(2, first.Data!.Scanned);
        Assert.Equal(2, first.Data.Generated);
        Assert.Equal(1, first.Data.Remaining);

        var second = await service.BackfillThumbnailsAsync(maxFiles: 2);
        Assert.Equal(1, second.Data!.Generated);
        Assert.Equal(0, second.Data.Remaining);
    }

    [Fact]
    public async Task BackfillThumbnailsAsync_FailuresAreCounted_AndStayCandidates()
    {
        var rasterizer = new FakePdfRasterizer { ThrowOnRender = new InvalidOperationException("password protected") };
        var service = CreateStorageService(options: ThumbnailOptions(), pdfRasterizer: rasterizer);
        var locked = await CreateStoredFileAsync("locked.pdf", PdfBytes);
        DbContext.ChangeTracker.Clear();

        var result = await service.BackfillThumbnailsAsync();

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(1, result.Data!.Scanned);
        Assert.Equal(0, result.Data.Generated);
        Assert.Equal(1, result.Data.Failed);
        Assert.Equal(1, result.Data.Remaining);
        Assert.Equal(new[] { locked.Id }, result.Data.FailedFileIds);
        DbContext.ChangeTracker.Clear();
        Assert.Null(DbContext.FileRecords.Single(f => f.Id == locked.Id).ThumbnailPath);
    }

    [Fact]
    public async Task BackfillThumbnailsAsync_WithoutThePackage_SkipsPdfsButStillDrawsBitmaps()
    {
        var service = CreateStorageService(options: ThumbnailOptions());
        var pdf = await CreateStoredFileAsync("a.pdf", PdfBytes);
        var png = await CreateStoredFileAsync("a.png", TinyPng(10, 10));
        DbContext.ChangeTracker.Clear();

        var result = await service.BackfillThumbnailsAsync();

        // PDF 不是候选（此刻画不出来），不算 Scanned 也不算 Failed
        Assert.Equal(1, result.Data!.Scanned);
        Assert.Equal(1, result.Data.Generated);
        Assert.Equal(0, result.Data.Failed);
        DbContext.ChangeTracker.Clear();
        Assert.Null(DbContext.FileRecords.Single(f => f.Id == pdf.Id).ThumbnailPath);
        Assert.False(string.IsNullOrEmpty(DbContext.FileRecords.Single(f => f.Id == png.Id).ThumbnailPath));
    }

    [Fact]
    public async Task BackfillThumbnailsAsync_IgnoresAutoGenerateThumbnail()
    {
        // 回填是管理员显式要求的；上传时的自动开关关着不该拦它
        var rasterizer = new FakePdfRasterizer();
        var service = CreateStorageService(
            options: ThumbnailOptions(o => o.AutoGenerateThumbnail = false),
            pdfRasterizer: rasterizer);
        await CreateStoredFileAsync("a.pdf", PdfBytes);
        DbContext.ChangeTracker.Clear();

        var result = await service.BackfillThumbnailsAsync();

        Assert.Equal(1, result.Data!.Generated);
    }

    // ---------- 生成器自身 ----------

    [Fact]
    public void SupportedExtensions_ListsPdfOnlyWhenItCanActuallyBeDrawn()
    {
        var monitor = new StaticOptionsMonitor<StorageOptions>(ThumbnailOptions());

        var none = new FileThumbnailGenerator(Storage, monitor);
        var unavailable = new FileThumbnailGenerator(Storage, monitor, pdfRasterizer: new FakePdfRasterizer { Available = false });
        var available = new FileThumbnailGenerator(Storage, monitor, pdfRasterizer: new FakePdfRasterizer());
        var switchedOff = new FileThumbnailGenerator(
            Storage, new StaticOptionsMonitor<StorageOptions>(ThumbnailOptions(o => o.PdfThumbnail.Enabled = false)),
            pdfRasterizer: new FakePdfRasterizer());

        Assert.DoesNotContain(".pdf", none.SupportedExtensions);
        Assert.DoesNotContain(".pdf", unavailable.SupportedExtensions);
        Assert.DoesNotContain(".pdf", switchedOff.SupportedExtensions);
        Assert.Contains(".pdf", available.SupportedExtensions);

        // 位图一侧与 FileTypeHelper 同源，不受 PDF 一侧影响
        Assert.Contains(".png", none.SupportedExtensions);
        Assert.Contains(".png", available.SupportedExtensions);
        Assert.DoesNotContain(".svg", available.SupportedExtensions);
        Assert.DoesNotContain(".heic", available.SupportedExtensions);

        Assert.True(available.CanGenerate(".PDF"));
        Assert.False(none.CanGenerate(".pdf"));
        Assert.False(available.CanGenerate(".docx"));
        Assert.False(available.CanGenerate(null));
    }

    [Fact]
    public async Task GenerateAsync_UnsupportedExtension_ReturnsNullWithoutTouchingStorage()
    {
        var recorder = new RecordingFileStorage(Storage);
        var generator = new FileThumbnailGenerator(recorder, new StaticOptionsMonitor<StorageOptions>(ThumbnailOptions()));

        var path = await generator.GenerateAsync("some/path", "key.docx", ".docx", 10);

        Assert.Null(path);
        Assert.Empty(recorder.UploadedKeys);
    }

    [Fact]
    public async Task GenerateAsync_ThumbnailKeyIsDerivedFromTheOriginalKey()
    {
        var recorder = new RecordingFileStorage(Storage);
        var generator = new FileThumbnailGenerator(
            recorder, new StaticOptionsMonitor<StorageOptions>(ThumbnailOptions()), pdfRasterizer: new FakePdfRasterizer());
        var originalPath = await Storage.UploadAsync("original.pdf", new MemoryStream(PdfBytes), "application/pdf");

        var path = await generator.GenerateAsync(originalPath, "original.pdf", ".pdf", PdfBytes.Length);

        Assert.NotNull(path);
        Assert.Equal(new[] { StorageKeyHelper.ThumbnailKey("original.pdf") }, recorder.UploadedKeys);
    }

    // ---------- DI 解析：可选注入的地基 ----------

    [Fact]
    public void TheContainerResolvesTheGenerator_WithoutTheOptionalRasterizerRegistered()
    {
        // 上面那些用例全是手工 new 出来的，绕过了容器；没加载 Tnzi.Documents 时 IPdfRasterizer 根本没注册，
        // 内置容器必须能靠构造函数默认值把生成器造出来 —— 造不出来的话每一次上传都会 500。
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IFileStorage>(Storage);
        services.AddSingleton<IOptionsMonitor<StorageOptions>>(new StaticOptionsMonitor<StorageOptions>(ThumbnailOptions()));
        services.AddScoped<IFileThumbnailGenerator, FileThumbnailGenerator>();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var sut = scope.ServiceProvider.GetRequiredService<IFileThumbnailGenerator>();

        Assert.False(sut.CanGenerate(".pdf"));
        Assert.True(sut.CanGenerate(".png"));
    }

    [Fact]
    public void TheContainerInjectsTheRasterizer_WhenTheOptionalPackageIsLoaded()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IFileStorage>(Storage);
        services.AddSingleton<IOptionsMonitor<StorageOptions>>(new StaticOptionsMonitor<StorageOptions>(ThumbnailOptions()));
        services.AddSingleton<IPdfRasterizer>(new FakePdfRasterizer());
        services.AddScoped<IFileThumbnailGenerator, FileThumbnailGenerator>();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var sut = scope.ServiceProvider.GetRequiredService<IFileThumbnailGenerator>();

        Assert.True(sut.CanGenerate(".pdf"));
    }

    // ---------- 配置校验 ----------

    [Theory]
    [InlineData(0)]
    [InlineData(1201)]
    public void Validator_RejectsDpiOutsideTheRasterizerRange(int dpi)
    {
        var validator = new StorageOptionsValidator();
        var options = ThumbnailOptions(o => o.PdfThumbnail.Dpi = dpi);

        var result = validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("PdfThumbnail.Dpi"));
    }

    [Fact]
    public void Validator_RejectsNegativeMaxSourceBytes_AndEmptyBox()
    {
        var validator = new StorageOptionsValidator();

        var negative = validator.Validate(null, ThumbnailOptions(o => o.PdfThumbnail.MaxSourceBytes = -1));
        var flat = validator.Validate(null, ThumbnailOptions(o => o.PdfThumbnail.MaxHeight = 0));
        var fine = validator.Validate(null, ThumbnailOptions());

        Assert.Contains(negative.Failures!, f => f.Contains("MaxSourceBytes"));
        Assert.Contains(flat.Failures!, f => f.Contains("PdfThumbnail.MaxWidth and MaxHeight"));
        Assert.True(fine.Succeeded);
    }

    private static byte[] TinyPng(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(200, 30, 30));
        using var buffer = new MemoryStream();
        image.SaveAsPng(buffer);
        return buffer.ToArray();
    }
}
