using Tnzi.Storage.Helpers;

namespace Tnzi.Storage.Tests;

/// <summary>
/// 扩展名到内容类型的映射表。
/// </summary>
/// <remarks>
/// <para>
/// 这张表看着像装饰性代码，实际是<b>整条内容类型链的源头</b>：
/// <c>GetContentType</c> 认不出来就回落 <c>application/octet-stream</c>，
/// 而下游任何按 <c>image/*</c> 分支的功能（预览、缩略图、收据识别）都会把它当二进制附件拒掉。
/// </para>
/// <para>
/// ★ 缺失 <c>.heic</c>/<c>.heif</c>（iOS 相机默认）与 <c>.tif</c>/<c>.tiff</c>（扫描仪默认）
/// 的实际后果是「手机拍的收据与扫描件开箱即拒」—— <b>而那正是这些功能最主要的输入来源</b>。
/// 表里少一行不会让任何测试变红，所以这张表需要一条明确的清单守着。
/// </para>
/// </remarks>
public class FileTypeHelperTests
{
    [Theory]
    // 传统位图
    [InlineData(".jpg", "image/jpeg")]
    [InlineData(".jpeg", "image/jpeg")]
    [InlineData(".png", "image/png")]
    [InlineData(".gif", "image/gif")]
    [InlineData(".bmp", "image/bmp")]
    [InlineData(".webp", "image/webp")]
    // 手机与扫描仪的默认格式
    [InlineData(".heic", "image/heic")]
    [InlineData(".heif", "image/heif")]
    [InlineData(".avif", "image/avif")]
    [InlineData(".tif", "image/tiff")]
    [InlineData(".tiff", "image/tiff")]
    public void Image_extensions_map_to_an_image_content_type(string extension, string expected)
    {
        Assert.Equal(expected, FileTypeHelper.GetContentType(extension));
        Assert.True(FileTypeHelper.IsImage(extension), $"{extension} should be recognized as an image");
    }

    /// <summary>大小写不敏感：文件名里的扩展名大小写由上传者决定。</summary>
    [Theory]
    [InlineData(".HEIC")]
    [InlineData(".Tiff")]
    [InlineData(".JPG")]
    public void Image_extension_matching_is_case_insensitive(string extension)
    {
        Assert.True(FileTypeHelper.IsImage(extension), $"{extension} should be recognized as an image");
        Assert.StartsWith("image/", FileTypeHelper.GetContentType(extension), StringComparison.Ordinal);
    }

    /// <summary>
    /// 对照：不认识的扩展名仍然回落二进制，而<b>不是</b>被猜成图片。
    /// </summary>
    /// <remarks>
    /// 没有这一条，把 <c>IsImage</c> 写成恒真也一样绿。
    /// </remarks>
    [Theory]
    [InlineData(".exe")]
    [InlineData(".unknown")]
    [InlineData("")]
    public void Unknown_extensions_stay_binary_and_are_not_images(string extension)
    {
        Assert.False(FileTypeHelper.IsImage(extension), $"{extension} should not be an image");
        Assert.Equal("application/octet-stream", FileTypeHelper.GetContentType(extension));
    }

    /// <summary>认得出来但不是图片的格式，不得因为「表里有」就被判成图片。</summary>
    [Theory]
    [InlineData(".docx")]
    [InlineData(".xlsx")]
    [InlineData(".mp4")]
    [InlineData(".zip")]
    public void Known_non_image_extensions_are_not_images(string extension)
    {
        Assert.False(FileTypeHelper.IsImage(extension), $"{extension} should not be an image");
        Assert.DoesNotContain("image/", FileTypeHelper.GetContentType(extension), StringComparison.Ordinal);
    }

    /// <summary>
    /// 「是不是图片」与「解得开吗」是两个正交的问题。
    /// </summary>
    /// <remarks>
    /// ★ 混用的后果是**可预期的失败被当成异常记录**：`.heic` 是 iOS 相机默认格式，
    /// 每张上传的照片都会让缩略图解码抛一次并在日志里留一条 ERROR，
    /// 而那既不是错误也没有人能处理。`.svg` 在本方法出现之前就一直是这样。
    /// 这与 `Tnzi.Documents` 的 `CanConvert` vs `IsAvailable` 是同一条教训。
    /// </remarks>
    [Theory]
    [InlineData(".jpg")]
    [InlineData(".png")]
    [InlineData(".gif")]
    [InlineData(".bmp")]
    [InlineData(".webp")]
    [InlineData(".tif")]      // ImageSharp 3.1 支持 TIFF —— 扫描件现在真的能出缩略图
    [InlineData(".tiff")]
    public void Decodable_image_formats_are_thumbnailable(string extension)
    {
        Assert.True(FileTypeHelper.IsImage(extension));
        Assert.True(FileTypeHelper.IsThumbnailable(extension), $"{extension} should be thumbnailable");
    }

    [Theory]
    [InlineData(".svg")]      // 矢量，ImageSharp 从来不支持
    [InlineData(".heic")]     // 需要额外编解码器（框架锁在 3.1.x 免授权线）
    [InlineData(".heif")]
    [InlineData(".avif")]
    public void Image_formats_the_decoder_cannot_read_are_not_thumbnailable(string extension)
    {
        // 仍然是图片（Content-Type 与呈现方式照旧），只是解不开
        Assert.True(FileTypeHelper.IsImage(extension), $"{extension} is still an image");
        Assert.False(FileTypeHelper.IsThumbnailable(extension), $"{extension} must not be thumbnailable");
    }

    /// <summary>非图片一律不可缩略 —— 别让它变成第二条「什么都算图片」的路。</summary>
    [Theory]
    [InlineData(".pdf")]
    [InlineData(".docx")]
    [InlineData(".unknown")]
    [InlineData("")]
    public void Non_images_are_never_thumbnailable(string extension)
    {
        Assert.False(FileTypeHelper.IsThumbnailable(extension));
    }

    [Fact]
    public void Pdf_is_recognized_separately_from_images()
    {
        Assert.True(FileTypeHelper.IsPdf(".pdf"));
        Assert.False(FileTypeHelper.IsImage(".pdf"));
        Assert.Equal("application/pdf", FileTypeHelper.GetContentType(".pdf"));
    }
}
