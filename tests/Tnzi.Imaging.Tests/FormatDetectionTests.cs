
namespace Tnzi.Imaging.Tests;

/// <summary>
/// Image format detection and perceptual hash 单元测试
/// </summary>
public class FormatDetectionTests
{
    #region DetectFormat - bytes

    [Fact]
    public void DetectFormat_Png_ReturnsCorrectInfo()
    {
        // PNG magic bytes: 89 50 4E 47 0D 0A 1A 0A
        var pngBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x00 };

        var result = BitmapExtensions.DetectFormat(pngBytes);

        result.ShouldNotBeNull();
        result.FormatName.ShouldBe("PNG");
        result.MimeType.ShouldBe("image/png");
        result.FileExtension.ShouldBe(".png");
    }

    [Fact]
    public void DetectFormat_Jpeg_ReturnsCorrectInfo()
    {
        var jpegBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46 };

        var result = BitmapExtensions.DetectFormat(jpegBytes);

        result.ShouldNotBeNull();
        result.FormatName.ShouldBe("JPEG");
        result.MimeType.ShouldBe("image/jpeg");
        result.FileExtension.ShouldBe(".jpg");
    }

    [Fact]
    public void DetectFormat_Gif_ReturnsCorrectInfo()
    {
        // GIF87a
        var gifBytes = new byte[] { 0x47, 0x49, 0x46, 0x38, 0x37, 0x61 };

        var result = BitmapExtensions.DetectFormat(gifBytes);

        result.ShouldNotBeNull();
        result.FormatName.ShouldBe("GIF");
        result.MimeType.ShouldBe("image/gif");
        result.FileExtension.ShouldBe(".gif");
    }

    [Fact]
    public void DetectFormat_Bmp_ReturnsCorrectInfo()
    {
        var bmpBytes = new byte[] { 0x42, 0x4D, 0x00, 0x00, 0x00, 0x00 };

        var result = BitmapExtensions.DetectFormat(bmpBytes);

        result.ShouldNotBeNull();
        result.FormatName.ShouldBe("BMP");
        result.MimeType.ShouldBe("image/bmp");
        result.FileExtension.ShouldBe(".bmp");
    }

    [Fact]
    public void DetectFormat_WebP_ReturnsCorrectInfo()
    {
        // RIFF....WEBP
        var webpBytes = new byte[] { 0x52, 0x49, 0x46, 0x46, 0x00, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50 };

        var result = BitmapExtensions.DetectFormat(webpBytes);

        result.ShouldNotBeNull();
        result.FormatName.ShouldBe("WebP");
        result.MimeType.ShouldBe("image/webp");
        result.FileExtension.ShouldBe(".webp");
    }

    [Fact]
    public void DetectFormat_TiffLittleEndian_ReturnsCorrectInfo()
    {
        var tiffBytes = new byte[] { 0x49, 0x49, 0x2A, 0x00, 0x00, 0x00 };

        var result = BitmapExtensions.DetectFormat(tiffBytes);

        result.ShouldNotBeNull();
        result.FormatName.ShouldBe("TIFF");
        result.MimeType.ShouldBe("image/tiff");
        result.FileExtension.ShouldBe(".tiff");
    }

    [Fact]
    public void DetectFormat_TiffBigEndian_ReturnsCorrectInfo()
    {
        var tiffBytes = new byte[] { 0x4D, 0x4D, 0x00, 0x2A, 0x00, 0x00 };

        var result = BitmapExtensions.DetectFormat(tiffBytes);

        result.ShouldNotBeNull();
        result.FormatName.ShouldBe("TIFF");
    }

    [Fact]
    public void DetectFormat_Ico_ReturnsCorrectInfo()
    {
        var icoBytes = new byte[] { 0x00, 0x00, 0x01, 0x00, 0x01, 0x00 };

        var result = BitmapExtensions.DetectFormat(icoBytes);

        result.ShouldNotBeNull();
        result.FormatName.ShouldBe("ICO");
        result.MimeType.ShouldBe("image/x-icon");
        result.FileExtension.ShouldBe(".ico");
    }

    [Fact]
    public void DetectFormat_UnknownFormat_ReturnsNull()
    {
        var unknownBytes = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04 };

        var result = BitmapExtensions.DetectFormat(unknownBytes);

        result.ShouldBeNull();
    }

    [Fact]
    public void DetectFormat_TooFewBytes_ReturnsNull()
    {
        var shortBytes = new byte[] { 0xFF, 0xD8 };

        var result = BitmapExtensions.DetectFormat(shortBytes);

        result.ShouldBeNull();
    }

    #endregion

    #region DetectFormat - stream

    [Fact]
    public void DetectFormat_Stream_DetectsFormat()
    {
        var pngBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x00 };
        using var stream = new MemoryStream(pngBytes);

        var result = BitmapExtensions.DetectFormat(stream);

        result.ShouldNotBeNull();
        result.FormatName.ShouldBe("PNG");
    }

    [Fact]
    public void DetectFormat_Stream_RestoresPosition()
    {
        var pngBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x00 };
        using var stream = new MemoryStream(pngBytes);
        stream.Position = 5;

        BitmapExtensions.DetectFormat(stream);

        stream.Position.ShouldBe(5);
    }

    [Fact]
    public void DetectFormat_NonSeekableStream_ThrowsArgumentException()
    {
        var nonSeekable = new NonSeekableStream();

        Should.Throw<ArgumentException>(() => BitmapExtensions.DetectFormat(nonSeekable));
    }

    #endregion

    #region Perceptual Hash

    [Fact]
    public void ComputePerceptualHash_ReturnsHexString()
    {
        using var image = new Image<Rgba32>(100, 100, Color.Red);

        var hash = image.ComputePerceptualHash();

        hash.ShouldNotBeNullOrEmpty();
        hash.Length.ShouldBe(16);
    }

    [Fact]
    public void ComputePerceptualHash_SameImage_SameHash()
    {
        using var image1 = new Image<Rgba32>(100, 100, Color.Blue);
        using var image2 = new Image<Rgba32>(100, 100, Color.Blue);

        var hash1 = image1.ComputePerceptualHash();
        var hash2 = image2.ComputePerceptualHash();

        hash1.ShouldBe(hash2);
    }

    [Fact]
    public void ComputePerceptualHash_DifferentImages_DifferentHash()
    {
        // Use images with different visual patterns (not solid colors, which all hash to the same value)
        using var image1 = new Image<Rgba32>(100, 100);
        // Top half white, bottom half black
        for (var y = 0; y < 50; y++)
            for (var x = 0; x < 100; x++)
                image1[x, y] = Color.White;
        for (var y = 50; y < 100; y++)
            for (var x = 0; x < 100; x++)
                image1[x, y] = Color.Black;

        using var image2 = new Image<Rgba32>(100, 100);
        // Left half white, right half black
        for (var y = 0; y < 100; y++)
        {
            for (var x = 0; x < 50; x++)
                image2[x, y] = Color.White;
            for (var x = 50; x < 100; x++)
                image2[x, y] = Color.Black;
        }

        var hash1 = image1.ComputePerceptualHash();
        var hash2 = image2.ComputePerceptualHash();

        hash1.ShouldNotBe(hash2);
    }

    [Fact]
    public void ComputePerceptualHash_SolidColor_ReturnsConsistentHash()
    {
        // Solid color images should have all pixels equal to mean, so hash = 0
        using var image = new Image<Rgba32>(100, 100, Color.Red);
        var hash = image.ComputePerceptualHash();

        hash.ShouldBe("0000000000000000");
    }

    [Fact]
    public void HammingDistance_IdenticalHashes_ReturnsZero()
    {
        using var image = new Image<Rgba32>(50, 50);
        // Create pattern so hash is non-zero
        for (var y = 0; y < 25; y++)
            for (var x = 0; x < 50; x++)
                image[x, y] = Color.White;
        for (var y = 25; y < 50; y++)
            for (var x = 0; x < 50; x++)
                image[x, y] = Color.Black;

        var hash = image.ComputePerceptualHash();

        var distance = BitmapExtensions.HammingDistance(hash, hash);

        distance.ShouldBe(0);
    }

    [Fact]
    public void HammingDistance_DifferentHashes_ReturnsPositive()
    {
        using var image1 = new Image<Rgba32>(100, 100);
        // Top-left quadrant white, rest black
        for (var y = 0; y < 50; y++)
            for (var x = 0; x < 50; x++)
                image1[x, y] = Color.White;

        using var image2 = new Image<Rgba32>(100, 100);
        // Bottom-right quadrant white, rest black
        for (var y = 50; y < 100; y++)
            for (var x = 50; x < 100; x++)
                image2[x, y] = Color.White;

        var hash1 = image1.ComputePerceptualHash();
        var hash2 = image2.ComputePerceptualHash();

        var distance = BitmapExtensions.HammingDistance(hash1, hash2);

        distance.ShouldBeGreaterThan(0);
        distance.ShouldBeLessThanOrEqualTo(64);
    }

    [Fact]
    public void HammingDistance_InvalidHashLength_ThrowsArgumentException()
    {
        Should.Throw<ArgumentException>(() => BitmapExtensions.HammingDistance("abc", "def"));
    }

    [Fact]
    public void HammingDistance_MaxDifference_Returns64()
    {
        // All zeros vs all ones
        var hash1 = "0000000000000000";
        var hash2 = "ffffffffffffffff";

        var distance = BitmapExtensions.HammingDistance(hash1, hash2);

        distance.ShouldBe(64);
    }

    #endregion

    /// <summary>
    /// Non-seekable stream for testing
    /// </summary>
    private class NonSeekableStream : MemoryStream
    {
        public override bool CanSeek => false;
    }
}
