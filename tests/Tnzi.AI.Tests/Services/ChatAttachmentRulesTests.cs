using System.Text;

namespace Tnzi.AI.Tests.Services;

/// <summary>
/// What a chat message to an external CLI agent may carry: inline images and PDFs, within the configured limits.
/// Anything else is refused with a code, never dropped: a turn that silently sent only its text looks exactly like
/// a turn whose attachment the model saw.
/// </summary>
public class ChatAttachmentRulesTests
{
    private static readonly ChatAttachmentOptions Limits = new() { MaxBytes = 1024, MaxCount = 3 };

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00];
    private static readonly byte[] Gif = Encoding.ASCII.GetBytes("GIF89a....");
    private static readonly byte[] Webp = Encoding.ASCII.GetBytes("RIFF\0\0\0\0WEBPVP8 ");
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n%%EOF");

    [Theory]
    [MemberData(nameof(SupportedFiles))]
    public void SupportedTypes_AreRecognisedFromTheirBytes_NotFromTheDeclaredType(byte[] bytes, string expected)
    {
        var read = ChatAttachmentRules.Read(
            [new ImageContentPartDto { Base64Data = Convert.ToBase64String(bytes), MediaType = "application/octet-stream" }], Limits);

        read.Succeeded.ShouldBeTrue(read.Message);
        read.Data!.Single().MediaType.ShouldBe(expected);
    }

    public static TheoryData<byte[], string> SupportedFiles() => new()
    {
        { Png, "image/png" },
        { Jpeg, "image/jpeg" },
        { Gif, "image/gif" },
        { Webp, "image/webp" },
        { Pdf, "application/pdf" }
    };

    [Fact]
    public void Documents_AndDataUris_AreRead()
    {
        var read = ChatAttachmentRules.Read(
        [
            new TextContentPartDto { Text = "see attached" },
            new DocumentContentPartDto { Base64Data = "data:application/pdf;base64," + Convert.ToBase64String(Pdf), FileName = @"C:\docs\..\contract.pdf" },
            new ImageContentPartDto { Url = "data:image/png;base64," + Convert.ToBase64String(Png) }
        ], Limits);

        read.Succeeded.ShouldBeTrue(read.Message);
        var data = read.Data.ShouldNotBeNull();
        data.Select(a => a.MediaType).ShouldBe(["application/pdf", "image/png"], "text parts are not attachments");
        data[0].FileName.ShouldBe("contract.pdf", "only the file name itself is kept");
        data[0].Content.ShouldBe(Pdf);
    }

    [Fact]
    public void NoParts_IsNoAttachments()
    {
        ChatAttachmentRules.Read(null, Limits).Data.ShouldBeEmpty();
        ChatAttachmentRules.Read([new TextContentPartDto { Text = "hi" }], Limits).Data.ShouldBeEmpty();
    }

    [Fact]
    public void AnUnsupportedType_IsRefusedWith415()
    {
        var read = ChatAttachmentRules.Read([new ImageContentPartDto { Base64Data = Convert.ToBase64String([0x42, 0x4D, 0, 0]) }], Limits);

        read.Succeeded.ShouldBeFalse();
        read.Code.ShouldBe(415);
        read.ErrorCode.ShouldBe(ErrorCodes.UnsupportedMediaType);
    }

    [Fact]
    public void AnImageUrl_IsRefused_TheServerDoesNotFetchIt()
    {
        var read = ChatAttachmentRules.Read([new ImageContentPartDto { Url = "https://example.com/cat.png" }], Limits);

        read.Code.ShouldBe(415);
        read.ErrorCode.ShouldBe(ErrorCodes.UnsupportedMediaType);
    }

    [Fact]
    public void AStorageFileReference_IsRefused()
    {
        var read = ChatAttachmentRules.Read([new FileContentPartDto { FileId = Guid.NewGuid() }], Limits);

        read.Code.ShouldBe(415);
    }

    [Fact]
    public void AnAttachmentOverTheSizeLimit_IsRefusedWith413()
    {
        var read = ChatAttachmentRules.Read(
            [new ImageContentPartDto { Base64Data = Convert.ToBase64String(Png.Concat(new byte[2048]).ToArray()), FileName = "big.png" }], Limits);

        read.Code.ShouldBe(413);
        read.ErrorCode.ShouldBe(ErrorCodes.AttachmentTooLarge);
        read.Message.ShouldNotBeNull().ShouldContain("big.png");
    }

    [Fact]
    public void MoreAttachmentsThanAllowed_AreRefused()
    {
        var part = new ImageContentPartDto { Base64Data = Convert.ToBase64String(Png) };

        var read = ChatAttachmentRules.Read([part, part, part, part], Limits);

        read.Code.ShouldBe(400);
        read.ErrorCode.ShouldBe(ErrorCodes.TooManyAttachments);
    }

    [Fact]
    public void InvalidBase64_IsABadRequest()
    {
        var read = ChatAttachmentRules.Read([new DocumentContentPartDto { Base64Data = "not base64 !!" }], Limits);

        read.Code.ShouldBe(400);
        read.ErrorCode.ShouldBe(ErrorCodes.InvalidContent);
    }

    [Fact]
    public void DetectMediaType_KnowsOnlyTheAcceptedTypes()
    {
        ChatAttachmentRules.DetectMediaType(Png).ShouldBe("image/png");
        ChatAttachmentRules.DetectMediaType(Encoding.ASCII.GetBytes("<svg xmlns=")).ShouldBeNull("anything that could run script in a browser is not an image here");
        ChatAttachmentRules.DetectMediaType([]).ShouldBeNull();
    }
}
