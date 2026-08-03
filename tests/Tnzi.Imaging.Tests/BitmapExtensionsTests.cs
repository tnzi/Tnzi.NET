
namespace Tnzi.Imaging.Tests;

public class BitmapExtensionsTests
{
    private Image<Rgba32> CreateTestImage(int width = 100, int height = 100)
    {
        return new Image<Rgba32>(width, height);
    }

    // ==================== Load Tests ====================

    [Fact]
    public void LoadFromBytes_ValidPng_ReturnsImage()
    {
        using var original = CreateTestImage(50, 50);
        using var ms = new MemoryStream();
        original.SaveAsPng(ms);
        var bytes = ms.ToArray();

        using var loaded = BitmapExtensions.LoadFromBytes(bytes);
        loaded.Width.ShouldBe(50);
        loaded.Height.ShouldBe(50);
    }

    [Fact]
    public void LoadFromStream_ValidPng_ReturnsImage()
    {
        using var original = CreateTestImage(60, 40);
        using var ms = new MemoryStream();
        original.SaveAsPng(ms);
        ms.Position = 0;

        using var loaded = BitmapExtensions.LoadFromStream(ms);
        loaded.Width.ShouldBe(60);
        loaded.Height.ShouldBe(40);
    }

    [Fact]
    public async Task LoadFromBytesAsync_ValidPng_ReturnsImage()
    {
        using var original = CreateTestImage(30, 30);
        using var ms = new MemoryStream();
        original.SaveAsPng(ms);
        var bytes = ms.ToArray();

        using var loaded = await BitmapExtensions.LoadFromBytesAsync(bytes);
        loaded.Width.ShouldBe(30);
        loaded.Height.ShouldBe(30);
    }

    [Fact]
    public async Task LoadFromStreamAsync_ValidPng_ReturnsImage()
    {
        using var original = CreateTestImage(45, 35);
        using var ms = new MemoryStream();
        original.SaveAsPng(ms);
        ms.Position = 0;

        using var loaded = await BitmapExtensions.LoadFromStreamAsync(ms);
        loaded.Width.ShouldBe(45);
        loaded.Height.ShouldBe(35);
    }

    // ==================== Resize Tests ====================

    [Fact]
    public void Resize_ByDimensions_ReturnsCorrectSize()
    {
        using var image = CreateTestImage(200, 200);
        using var resized = image.Resize(100, 100);
        resized.Width.ShouldBeLessThanOrEqualTo(100);
        resized.Height.ShouldBeLessThanOrEqualTo(100);
    }

    [Fact]
    public void Resize_ByScale_ReturnsScaledSize()
    {
        using var image = CreateTestImage(200, 100);
        using var resized = image.Resize(0.5f);
        resized.Width.ShouldBe(100);
        resized.Height.ShouldBe(50);
    }

    [Fact]
    public void Resize_InvalidScale_Throws()
    {
        using var image = CreateTestImage();
        Should.Throw<ArgumentException>(() => image.Resize(0f));
        Should.Throw<ArgumentException>(() => image.Resize(1.5f));
    }

    [Fact]
    public void ResizeToFit_SmallerThanMax_ReturnsClone()
    {
        using var image = CreateTestImage(50, 50);
        using var result = image.ResizeToFit(100, 100);
        result.Width.ShouldBe(50);
        result.Height.ShouldBe(50);
    }

    [Fact]
    public void ResizeToFit_LargerThanMax_ScalesDown()
    {
        using var image = CreateTestImage(200, 100);
        using var result = image.ResizeToFit(100, 100);
        result.Width.ShouldBeLessThanOrEqualTo(100);
        result.Height.ShouldBeLessThanOrEqualTo(100);
    }

    // ==================== Crop Tests ====================

    [Fact]
    public void Crop_ValidRegion_ReturnsCroppedImage()
    {
        using var image = CreateTestImage(100, 100);
        using var cropped = image.Crop(10, 10, 50, 50);
        cropped.Width.ShouldBe(50);
        cropped.Height.ShouldBe(50);
    }

    [Fact]
    public void Crop_OutOfBounds_Throws()
    {
        using var image = CreateTestImage(100, 100);
        Should.Throw<ArgumentException>(() => image.Crop(80, 80, 50, 50));
    }

    [Fact]
    public void CropFromCenter_ReturnsCorrectSize()
    {
        using var image = CreateTestImage(100, 100);
        using var cropped = image.CropFromCenter(50, 50);
        cropped.Width.ShouldBe(50);
        cropped.Height.ShouldBe(50);
    }

    // ==================== Compress Tests ====================

    [Fact]
    public void CompressAsJpeg_ReturnsJpegBytes()
    {
        using var image = CreateTestImage();
        var bytes = image.CompressAsJpeg(85);
        bytes.ShouldNotBeEmpty();
        // JPEG magic bytes: 0xFF 0xD8
        bytes[0].ShouldBe((byte)0xFF);
        bytes[1].ShouldBe((byte)0xD8);
    }

    [Fact]
    public void CompressAsPng_ReturnsPngBytes()
    {
        using var image = CreateTestImage();
        var bytes = image.CompressAsPng();
        bytes.ShouldNotBeEmpty();
        // PNG magic bytes: 0x89 0x50 0x4E 0x47
        bytes[0].ShouldBe((byte)0x89);
        bytes[1].ShouldBe((byte)0x50);
    }

    // ==================== Format Conversion Tests ====================

    [Fact]
    public void ToJpeg_ReturnsJpegBytes()
    {
        using var image = CreateTestImage();
        var bytes = image.ToJpeg(90);
        bytes.ShouldNotBeEmpty();
        bytes[0].ShouldBe((byte)0xFF);
        bytes[1].ShouldBe((byte)0xD8);
    }

    [Fact]
    public void ToPng_ReturnsPngBytes()
    {
        using var image = CreateTestImage();
        var bytes = image.ToPng();
        bytes.ShouldNotBeEmpty();
        bytes[0].ShouldBe((byte)0x89);
    }

    [Fact]
    public void ToWebP_ReturnsWebPBytes()
    {
        using var image = CreateTestImage();
        var bytes = image.ToWebP(90);
        bytes.ShouldNotBeEmpty();
        // WebP magic: RIFF....WEBP
        bytes[0].ShouldBe((byte)'R');
        bytes[1].ShouldBe((byte)'I');
        bytes[2].ShouldBe((byte)'F');
        bytes[3].ShouldBe((byte)'F');
    }

    // ==================== Thumbnail Tests ====================

    [Fact]
    public void GenerateThumbnail_ReturnsScaledImage()
    {
        using var image = CreateTestImage(200, 200);
        using var thumb = image.GenerateThumbnail(50, 50);
        thumb.Width.ShouldBeLessThanOrEqualTo(50);
        thumb.Height.ShouldBeLessThanOrEqualTo(50);
    }

    [Fact]
    public void GenerateSquareThumbnail_ReturnsSquare()
    {
        using var image = CreateTestImage(200, 100);
        using var thumb = image.GenerateSquareThumbnail(50);
        thumb.Width.ShouldBe(50);
        thumb.Height.ShouldBe(50);
    }

    // ==================== Watermark Tests ====================

    [Fact]
    public void AddTextWatermark_DoesNotThrow()
    {
        using var image = CreateTestImage(200, 200);
        using var result = image.AddTextWatermark("Test", 16);
        result.ShouldNotBeNull();
        result.Width.ShouldBe(200);
        result.Height.ShouldBe(200);
    }

    [Fact]
    public void AddImageWatermark_DoesNotThrow()
    {
        using var image = CreateTestImage(200, 200);
        using var watermark = CreateTestImage(30, 30);
        using var result = image.AddImageWatermark(watermark, WatermarkPosition.Center, 0.5f);
        result.ShouldNotBeNull();
        result.Width.ShouldBe(200);
        result.Height.ShouldBe(200);
    }
}
