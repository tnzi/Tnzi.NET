using Tnzi.Imaging.Extensions;

namespace Tnzi.Documents.Tests;

/// <summary>
/// <see cref="IPdfRasterizer"/> 的第二类消费者：给人看的缩略图（<c>Tnzi.Storage</c> 为上传的 PDF 出首页缩略图）。
/// 那条链是「渲染器出 PNG → <c>Tnzi.Imaging</c> 的解码闸门解码 → 等比缩进盒子 → JPEG」；
/// Storage 自己的测试项目刻意不引用本包，只能拿替身驱动，所以**真 PDFium 的产物能不能被那一侧吃下**在这里守。
/// </summary>
/// <remarks>
/// 这些测试真的加载 PDFium 原生库，与 <see cref="PdfiumPdfRasterizerTests"/> 同一口径。
/// </remarks>
public class PdfRasterizerThumbnailHandoffTests
{
    private static PdfiumPdfRasterizer Create()
        => new(Microsoft.Extensions.Options.Options.Create(new PdfRasterOptions()), NullLogger<PdfiumPdfRasterizer>.Instance);

    [Fact]
    public async Task AColourPageAtThumbnailDpi_DecodesThroughTheImagingGuard_AndFitsTheBox()
    {
        // Storage 的默认值：100 dpi、彩色、600×800 的盒子。
        var pdf = TestPdfs.Blank(1);

        var page = await Create().RenderPageAsync(pdf, 0, new PdfRasterRequest { Dpi = 100, Grayscale = false });
        using var decoded = await ImageDecodeGuard.LoadAsync(page.Content);
        using var thumbnail = decoded.ResizeToFit(600, 800);

        // US Letter @ 100 dpi 是 850×1100（PDFium 可能少一个像素），缩进 600×800 受宽度约束
        decoded.Width.ShouldBeInRange(849, 850);
        decoded.Height.ShouldBeInRange(1099, 1100);
        thumbnail.Width.ShouldBeInRange(599, 600);
        thumbnail.Height.ShouldBeInRange(774, 777);

        // 编成 JPEG 得是一张真 JPEG：/thumbnail 端点恒以 image/jpeg 发出
        using var jpeg = new MemoryStream();
        await thumbnail.SaveAsJpegAsync(jpeg);
        jpeg.ToArray().Take(3).ShouldBe(new byte[] { 0xFF, 0xD8, 0xFF });
    }

    [Fact]
    public async Task ALandscapePage_KeepsBothEdgesWhenFittedIntoThePortraitBox()
    {
        // 一张 8.5×3.5 英寸的支票：整页缩进盒子，两侧与下边缘都要还在（不是中心裁剪）。
        var stamper = new PdfSharpPdfStamper(NullLogger<PdfSharpPdfStamper>.Instance);
        var cheque = stamper.Create(new PdfStampRequest
        {
            AppendPages = [new PdfPageSpec { WidthPoints = 612d, HeightPoints = 252d }],
        });

        var page = await Create().RenderPageAsync(cheque, 0, new PdfRasterRequest { Dpi = 100, Grayscale = false });
        using var decoded = await ImageDecodeGuard.LoadAsync(page.Content);
        using var thumbnail = decoded.ResizeToFit(600, 800);

        decoded.Width.ShouldBeInRange(849, 850);
        decoded.Height.ShouldBeInRange(349, 350);
        thumbnail.Width.ShouldBeInRange(599, 600);
        thumbnail.Height.ShouldBeInRange(246, 248);
        ((double)thumbnail.Width / thumbnail.Height).ShouldBe((double)decoded.Width / decoded.Height, 0.02);
    }
}
