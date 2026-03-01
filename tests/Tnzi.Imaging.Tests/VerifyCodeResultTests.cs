using Tnzi.Services;

namespace Tnzi.Imaging.Tests;

public class VerifyCodeResultTests
{
    [Fact]
    public void Base64Image_WithBytes_ReturnsBase64()
    {
        var result = new VerifyCodeResult
        {
            ImageBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 }
        };

        result.Base64Image.ShouldNotBeNullOrEmpty();
        // Verify it's valid base64
        var decoded = Convert.FromBase64String(result.Base64Image);
        decoded.ShouldBe(result.ImageBytes);
    }

    [Fact]
    public void Base64Image_EmptyBytes_ReturnsEmpty()
    {
        var result = new VerifyCodeResult();
        result.Base64Image.ShouldBeEmpty();
    }

    [Fact]
    public void DataUri_WithBytes_ReturnsDataUri()
    {
        var result = new VerifyCodeResult
        {
            ImageBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 }
        };

        result.DataUri.ShouldStartWith("data:image/png;base64,");
        result.DataUri.ShouldContain(result.Base64Image);
    }

    [Fact]
    public void DataUri_EmptyBytes_ReturnsEmpty()
    {
        var result = new VerifyCodeResult();
        result.DataUri.ShouldBeEmpty();
    }
}
