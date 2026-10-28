using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Tnzi.Documents.Exceptions;
using Tnzi.Documents.Models;
using Tnzi.Geometry;
using Tnzi.ScopedContext;
using Tnzi.Security.Claims;
using Tnzi.Signing.Dtos;
using Tnzi.Signing.Entities;
using Tnzi.Signing.Metadata;
using Tnzi.Signing.Services.Internal;

namespace Tnzi.Signing.Tests;

/// <summary>
/// 匿名端点收进来的自由文本：签名图、同意条款、拒签原因、字段值，以及浏览器自报的 User-Agent。
/// </summary>
/// <remarks>
/// <para>
/// 它们此前是列上无上限（或列有上限而服务不校验）的自由文本，而写入路径不要求任何登录 ——
/// 一个匿名可写的无界字段就是存储滥用面，一个列有上限而服务不看的字段在 SQL Server 上是一个 500。
/// 现在上限写在列与服务两处，取同一个常量；收件人填的越界拒绝，浏览器自报的越界截断。
/// </para>
/// <para>
/// 形态校验放在提交那一刻是为了不让一次拼错的提交钉死整份请求：图存进去之后收件人就是 Signed、
/// 不能重交，而解不出来的图会让密封在盖章那一步失败，信封停在 InProgress，没有任何人能修。
/// </para>
/// </remarks>
public class SubmitSigningPayloadTests : SigningStorageTestBase
{
    private const string TinyPng =
        "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

    /// <summary>一张完整的小 JPEG：只有文件头的 JPEG 现在会被拒（见下面的残缺图用例）。</summary>
    private static readonly string TinyJpeg = TestImages.SmallJpegBase64;

    /// <summary>请求上下文：签署审计从这里取 IP 与 User-Agent。</summary>
    private sealed class TestScopedContext : Tnzi.ScopedContext.ScopedContext
    {
    }

    /// <summary>本组用例里的「浏览器」自报的 User-Agent；每个用例按需改。</summary>
    private string UserAgent { get; set; } = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)";

    protected override void ConfigureServices(IServiceCollection services)
    {
        var anonymous = new Mock<ICurrentUser>();
        anonymous.SetupGet(u => u.IsAuthenticated).Returns(false);
        anonymous.SetupGet(u => u.Id).Returns((Guid?)null);
        services.AddScoped(_ => anonymous.Object);
        services.AddScoped<IScopedContext>(_ => new TestScopedContext { ClientIpAddress = "203.0.113.7", UserAgent = UserAgent });
    }

    private async Task<Signer> SignerOfAsync(Guid requestId)
        => await DbContext.Set<Signer>().AsNoTracking().SingleAsync(s => s.RequestId == requestId);

    private static Field RecipientTextField(string key = "Notes", string label = "Notes") => new()
    {
        Key = key,
        Label = label,
        Type = SigningFieldType.Text,
        RecipientRole = "Client",
        PlacementMode = FieldPlacementMode.Absolute,
        Page = 1,
        X = 0.1m,
        Y = 0.1m,
        W = 0.5m,
        H = 0.04m,
    };

    // ── 形态 ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(TinyPng)]
    [InlineData("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==")]
    [InlineData("data:image/png;base64,iVBORw0KGgo\nAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==")]
    public void A_data_url_or_bare_base64_is_well_formed(string payload)
    {
        SignatureImagePayload.IsWellFormed(payload).ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("data:image/png,not-base64-marker")]
    [InlineData("data:image/png;base64,")]
    [InlineData("this is not base64!!")]
    [InlineData("<svg onload=alert(1)>")]
    public void Anything_that_does_not_decode_is_rejected(string payload)
    {
        SignatureImagePayload.IsWellFormed(payload).ShouldBeFalse();
    }

    /// <summary>
    /// 解得出字节不等于是一张图：<c>AAAA</c> 是合法 base64（三个零字节），签名板导出的 SVG / WEBP 也是。
    /// 盖章方只认 PNG 与 JPEG，别的都会在密封那一步炸 —— 而那时收件人已是 Signed。
    /// </summary>
    [Theory]
    [InlineData("data:image/png;base64,AAAA")]
    [InlineData("AAAA")]
    [InlineData("data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciLz4=")]
    [InlineData("data:image/webp;base64,UklGRiQAAABXRUJQVlA4IBgAAAAwAQCdASoBAAEAAwA0JaQAA3AA/vuUAAA=")]
    [InlineData("data:image/gif;base64,R0lGODlhAQABAAAAACH5BAEKAAEALAAAAAABAAEAAAICTAEAOw==")]
    public void Valid_base64_that_is_not_a_png_or_jpeg_is_rejected(string payload)
    {
        SignatureImagePayload.IsWellFormed(payload).ShouldBeFalse();
    }

    [Fact]
    public void A_jpeg_signature_is_accepted()
    {
        SignatureImagePayload.IsWellFormed("data:image/jpeg;base64," + TinyJpeg).ShouldBeTrue();
        SignatureImagePayload.IsWellFormed(TinyJpeg).ShouldBeTrue();
    }

    /// <summary>一份短于任何图片头的载荷不是图，也不能让嗅探越界。</summary>
    [Theory]
    [InlineData("AA==")]
    [InlineData("iVBORw0=")]
    public void A_payload_shorter_than_an_image_header_is_rejected(string payload)
    {
        SignatureImagePayload.IsWellFormed(payload).ShouldBeFalse();
    }

    // ── 残缺与损坏：文件头对，图不完整 ──────────────────────────────────────

    public static TheoryData<string, byte[]> BrokenImages()
    {
        var png = Convert.FromBase64String(TestImages.TinyPngBase64);
        var jpeg = TestImages.SmallJpeg;

        var badCrc = png.ToArray();
        badCrc[29] ^= 0xFF; // IHDR 的 CRC

        var badPixels = TestImages.Png(4, 4);
        badPixels[^20] ^= 0x5A; // IDAT 数据（CRC 随之对不上）

        return new TheoryData<string, byte[]>
        {
            { "bare PNG signature", png[..8] },
            { "PNG cut inside IHDR", png[..20] },
            { "PNG cut inside IDAT", png[..45] },
            { "PNG without IEND", png[..^12] },
            { "PNG with a corrupt CRC", badCrc },
            { "PNG with corrupt pixel data", badPixels },
            { "PNG whose image data is shorter than its size", TestImages.Png(16, 16, new byte[10]) },
            { "PNG whose image data is not a zlib stream (CRC intact)", TestImages.PngWithRawIdat(4, 4, [0x78, 0x9C, 0xFF, 0xFF, 0x00, 0x01]) },
            { "JPEG header only", [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00] },
            { "JPEG cut before the end-of-image marker", jpeg[..^2] },
            { "JPEG cut inside a marker segment", jpeg[..30] },
        };
    }

    /// <summary>
    /// 以合法文件头开始的残缺图（签名板库或代理截断了 data URL、只发了文件头、CRC 损坏）此前都能过校验：
    /// 存进去、Signed，密封时盖章方解不开而永远失败，只剩作废、让每个人重签一遍。
    /// </summary>
    [Theory]
    [MemberData(nameof(BrokenImages))]
    public void An_incomplete_or_corrupt_image_is_rejected(string because, byte[] image)
    {
        SignatureImagePayload.Inspect(Convert.ToBase64String(image)).ShouldBe(SignatureImageVerdict.Malformed, because);
    }

    [Fact]
    public void Complete_pngs_and_jpegs_are_accepted()
    {
        SignatureImagePayload.Inspect(TestImages.TinyPngBase64).ShouldBe(SignatureImageVerdict.Valid);
        SignatureImagePayload.Inspect(TestImages.SmallRgbPngBase64).ShouldBe(SignatureImageVerdict.Valid);
        SignatureImagePayload.Inspect(Convert.ToBase64String(TestImages.Png(600, 200))).ShouldBe(SignatureImageVerdict.Valid);
        SignatureImagePayload.Inspect(TestImages.SmallJpegBase64).ShouldBe(SignatureImageVerdict.Valid);
    }

    // ── 尺寸：文件头声明的像素数，早于任何解压 ────────────────────────────────

    public static TheoryData<string, byte[]> OversizedImages() => new()
    {
        // 几十个字节的图像数据声明 4 亿像素：整张解码要分配上 GB。
        { "20000 x 20000 PNG", TestImages.Png(20000, 20000, new byte[16]) },
        { "PNG wider than the side limit", TestImages.Png(SigningLimits.MaxSignatureImageSide + 1, 10, new byte[16]) },
        { "PNG over the pixel budget", TestImages.Png(SigningLimits.MaxSignatureImageSide, 1025, new byte[16]) },
        { "JPEG declaring 5000 x 100", TestImages.JpegDeclaring(5000, 100) },
    };

    [Theory]
    [MemberData(nameof(OversizedImages))]
    public void An_image_declaring_too_many_pixels_is_rejected_before_it_is_decoded(string because, byte[] image)
    {
        SignatureImagePayload.Inspect(Convert.ToBase64String(image)).ShouldBe(SignatureImageVerdict.TooLarge, because);
    }

    [Fact]
    public void An_image_at_the_limits_is_accepted()
    {
        var atLimit = TestImages.Png(SigningLimits.MaxSignatureImageSide, SigningLimits.MaxSignatureImagePixels / SigningLimits.MaxSignatureImageSide);

        SignatureImagePayload.Inspect(Convert.ToBase64String(atLimit)).ShouldBe(SignatureImageVerdict.Valid);
    }

    /// <summary>PNG 规范允许的每一种色彩类型 × 位深，隔行与不隔行各一。</summary>
    public static TheoryData<int, int, bool> AllPngVariants()
    {
        var data = new TheoryData<int, int, bool>();
        var depths = new Dictionary<int, int[]>
        {
            [0] = [1, 2, 4, 8, 16],
            [2] = [8, 16],
            [3] = [1, 2, 4, 8],
            [4] = [8, 16],
            [6] = [8, 16],
        };
        foreach (var (colorType, bitDepths) in depths)
        {
            foreach (var bitDepth in bitDepths)
            {
                data.Add(colorType, bitDepth, false);
                data.Add(colorType, bitDepth, true);
            }
        }

        return data;
    }

    private static bool RealStamperDecodes(string image)
    {
        try
        {
            RealStamper.Create(new PdfStampRequest
            {
                AppendPages = [new PdfPageSpec { WidthPoints = 300, HeightPoints = 100 }],
                Stamps = [new PdfImageStamp { PageNumber = 1, Rect = new NormalizedRect(0, 0, 1, 1), DataUrl = image }],
            });
            return true;
        }
        catch (PdfDocumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// 结构完整不等于盖章方解得开：PDFsharp 不认 1 / 2 / 4 / 16 位灰度、2 位调色板、隔行的 1 / 4 位调色板与全部 16 位 PNG。
    /// 这些若在提交时放行，密封会在盖章那一步失败、请求钉死。两边对每一种组合的判定必须一致：
    /// 盖章实现换版本、支持面变了，这里先红，提醒同步 <c>SignatureImagePayload</c>。
    /// </summary>
    [Theory]
    [MemberData(nameof(AllPngVariants))]
    public void The_validator_accepts_exactly_the_png_variants_the_stamper_can_draw(int colorType, int bitDepth, bool interlaced)
    {
        var image = Convert.ToBase64String(TestImages.PngOf(colorType, bitDepth, interlaced));

        var decodes = RealStamperDecodes(image);
        var verdict = SignatureImagePayload.Inspect(image);

        verdict.ShouldBe(decodes ? SignatureImageVerdict.Valid : SignatureImageVerdict.Unsupported);
    }

    [Theory]
    [InlineData(TestImages.SmallJpegBase64, true)]
    [InlineData(TestImages.ProgressiveRgbJpegBase64, true)]
    [InlineData(TestImages.CmykJpegBase64, false)]
    public void The_validator_accepts_exactly_the_jpeg_variants_the_stamper_can_draw(string image, bool stampable)
    {
        RealStamperDecodes(image).ShouldBe(stampable);
        SignatureImagePayload.Inspect(image).ShouldBe(stampable ? SignatureImageVerdict.Valid : SignatureImageVerdict.Unsupported);
    }

    [Fact]
    public async Task A_signature_image_the_stamper_cannot_draw_is_refused_at_submit()
    {
        var (_, requestId, token) = await ArrangeSentEnvelopeAsync();
        var sixteenBit = TestImages.DataUrl("image/png", TestImages.PngOf(colorType: 6, bitDepth: 16, interlaced: false));

        using var submit = BeginRequest();
        var result = await submit.Envelopes.SubmitAsync(token, new SubmitSigningDto { SignatureImage = sixteenBit });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message!.ShouldContain("cannot be stamped");
        (await SignerOfAsync(requestId)).Status.ShouldNotBe(SigningRecipientStatus.Signed);
    }

    /// <summary>
    /// 这道校验的全部意义是「放行的图，密封时一定盖得上」：用生产的盖章实现把每一张放行的素材真的画一遍。
    /// </summary>
    [Fact]
    public void Every_accepted_fixture_is_decoded_by_the_real_stamper()
    {
        string[] accepted =
        [
            TinyPng,
            TestImages.DataUrl("image/png", Convert.FromBase64String(TestImages.SmallRgbPngBase64)),
            TestImages.DataUrl("image/png", TestImages.Png(600, 200)),
            TestImages.DataUrl("image/jpeg", TestImages.SmallJpeg),
        ];

        foreach (var image in accepted)
        {
            SignatureImagePayload.IsWellFormed(image).ShouldBeTrue();
            var pdf = RealStamper.Create(new PdfStampRequest
            {
                AppendPages = [new PdfPageSpec { WidthPoints = 300, HeightPoints = 100 }],
                Stamps = [new PdfImageStamp { PageNumber = 1, Rect = new NormalizedRect(0, 0, 1, 1), DataUrl = image }],
            });
            pdf.Length.ShouldBeGreaterThan(0);
        }
    }

    // ── 提交路径 ──────────────────────────────────────────────────────────

    [Fact]
    public async Task An_oversized_signature_image_is_refused_before_anything_is_written()
    {
        var (_, requestId, token) = await ArrangeSentEnvelopeAsync();
        var oversized = "data:image/png;base64," + new string('A', SigningLimits.MaxSignatureImageLength);

        using var submit = BeginRequest();
        var result = await submit.Envelopes.SubmitAsync(token, new SubmitSigningDto { SignatureImage = oversized });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message!.ShouldContain("signature image");

        var signer = await SignerOfAsync(requestId);
        signer.Status.ShouldNotBe(SigningRecipientStatus.Signed);
        signer.SignatureImage.ShouldBeNull();
    }

    /// <summary>
    /// 两种「不是图」都在提交那一刻拦：解不出 base64 的，和解得出但不是 PNG / JPEG 的。
    /// 后者此前能过校验 —— 存进去、Signed、最后一位提交时密封在盖章那一步炸，信封钉死在 InProgress。
    /// </summary>
    [Theory]
    [InlineData("not an image at all")]
    [InlineData("data:image/png;base64,AAAA")]
    [InlineData("data:image/png;base64,iVBORw0KGgo=")]
    public async Task A_signature_image_that_is_not_a_png_or_jpeg_is_refused_at_submit_not_at_seal(string payload)
    {
        var (_, requestId, token) = await ArrangeSentEnvelopeAsync();

        using var submit = BeginRequest();
        var result = await submit.Envelopes.SubmitAsync(token, new SubmitSigningDto { SignatureImage = payload });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message!.ShouldContain("PNG or JPEG");

        // 关键在这里：请求没有被钉死。收件人仍可重交，信封仍是 Sent/Viewed 而不是一个永远密封不了的 InProgress。
        var envelope = await DbContext.Set<Envelope>().AsNoTracking().SingleAsync(e => e.Id == requestId);
        envelope.Status.ShouldNotBe(EnvelopeStatus.InProgress);
        (await SignerOfAsync(requestId)).Status.ShouldNotBe(SigningRecipientStatus.Signed);
    }

    /// <summary>
    /// 一张字符数在上限之内、却声明了上亿像素的图：提交时就拦（400），收件人仍可重交。
    /// 等到密封才解码，进程要为它分配上 GB，而每一次重新密封都会再来一遍。
    /// </summary>
    [Fact]
    public async Task A_signature_image_declaring_too_many_pixels_is_refused_at_submit()
    {
        var (_, requestId, token) = await ArrangeSentEnvelopeAsync();
        var bomb = TestImages.DataUrl("image/png", TestImages.Png(20000, 20000, new byte[16]));

        using var submit = BeginRequest();
        var result = await submit.Envelopes.SubmitAsync(token, new SubmitSigningDto { SignatureImage = bomb });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message!.ShouldContain("too large");
        (await SignerOfAsync(requestId)).Status.ShouldNotBe(SigningRecipientStatus.Signed);
    }

    [Fact]
    public async Task An_oversized_consent_text_is_refused()
    {
        var (_, _, token) = await ArrangeSentEnvelopeAsync();

        using var submit = BeginRequest();
        var result = await submit.Envelopes.SubmitAsync(token, new SubmitSigningDto
        {
            ConsentText = new string('x', SigningLimits.MaxConsentTextLength + 1),
        });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message!.ShouldContain("consent text");
    }

    /// <summary>上限之内、形态正确的提交照常签成 —— 校验不该把正常的签名板输出挡在外面。</summary>
    [Fact]
    public async Task A_well_formed_signature_within_the_limit_still_signs()
    {
        var (_, requestId, token) = await ArrangeSentEnvelopeAsync();

        using var submit = BeginRequest();
        var result = await submit.Envelopes.SubmitAsync(token, new SubmitSigningDto
        {
            SignatureImage = TinyPng,
            ConsentText = new string('x', SigningLimits.MaxConsentTextLength),
        });

        result.Succeeded.ShouldBeTrue(result.Message);
        (await SignerOfAsync(requestId)).SignatureImage.ShouldBe(TinyPng);
    }

    // ── 其余三个匿名可写的自由文本 ────────────────────────────────────────

    /// <summary>
    /// User-Agent 是浏览器自报的取证数据，不是收件人填的：越界截断而不是拒绝 ——
    /// 一个合法但很长的 WebView UA 不该让人签不了字。
    /// </summary>
    [Fact]
    public async Task An_overlong_user_agent_is_truncated_not_rejected()
    {
        UserAgent = new string('u', SigningLimits.MaxSignerUserAgentLength + 200);
        var (_, requestId, token) = await ArrangeSentEnvelopeAsync();

        using var submit = BeginRequest();
        var result = await submit.Envelopes.SubmitAsync(token, new SubmitSigningDto());

        result.Succeeded.ShouldBeTrue(result.Message);
        var signer = await SignerOfAsync(requestId);
        signer.Status.ShouldBe(SigningRecipientStatus.Signed);
        signer.SignerUserAgent!.Length.ShouldBe(SigningLimits.MaxSignerUserAgentLength);
    }

    [Fact]
    public async Task An_overlong_user_agent_is_truncated_on_decline_too()
    {
        UserAgent = new string('u', SigningLimits.MaxSignerUserAgentLength + 200);
        var (_, requestId, token) = await ArrangeSentEnvelopeAsync();

        using var decline = BeginRequest();
        var result = await decline.Envelopes.DeclineAsync(token, "Changed my mind");

        result.Succeeded.ShouldBeTrue(result.Message);
        (await SignerOfAsync(requestId)).SignerUserAgent!.Length.ShouldBe(SigningLimits.MaxSignerUserAgentLength);
    }

    /// <summary>拒签原因是收件人填的文本：越界得到一句可读的 400，状态不动。</summary>
    [Fact]
    public async Task An_overlong_decline_reason_is_refused()
    {
        var (_, requestId, token) = await ArrangeSentEnvelopeAsync();

        using var decline = BeginRequest();
        var result = await decline.Envelopes.DeclineAsync(token, new string('r', SigningLimits.MaxDeclineReasonLength + 1));

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message!.ShouldContain("reason");

        var envelope = await DbContext.Set<Envelope>().AsNoTracking().SingleAsync(e => e.Id == requestId);
        envelope.Status.ShouldNotBe(EnvelopeStatus.Declined);
        (await SignerOfAsync(requestId)).Status.ShouldNotBe(SigningRecipientStatus.Declined);
    }

    /// <summary>字段值是收件人填的、又会被画进成品：越界的值按字段标签指名拒绝，什么都不写。</summary>
    [Fact]
    public async Task An_overlong_field_value_is_refused()
    {
        var (_, requestId, token) = await ArrangeSentEnvelopeAsync([RecipientTextField("Notes", "Additional notes")]);

        using var submit = BeginRequest();
        var result = await submit.Envelopes.SubmitAsync(token, new SubmitSigningDto
        {
            Values = new Dictionary<string, string?> { ["Notes"] = new string('n', SigningLimits.MaxFieldValueLength + 1) },
        });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message!.ShouldContain("Additional notes");

        (await SignerOfAsync(requestId)).Status.ShouldNotBe(SigningRecipientStatus.Signed);
        (await DbContext.Set<FieldValue>().AsNoTracking().AnyAsync(v => v.RequestId == requestId)).ShouldBeFalse();
    }

    [Fact]
    public async Task A_field_value_at_the_limit_is_accepted()
    {
        var (_, requestId, token) = await ArrangeSentEnvelopeAsync([RecipientTextField()]);

        using var submit = BeginRequest();
        var result = await submit.Envelopes.SubmitAsync(token, new SubmitSigningDto
        {
            Values = new Dictionary<string, string?> { ["Notes"] = new string('n', SigningLimits.MaxFieldValueLength) },
        });

        result.Succeeded.ShouldBeTrue(result.Message);
        (await DbContext.Set<FieldValue>().AsNoTracking().SingleAsync(v => v.RequestId == requestId))
            .Value!.Length.ShouldBe(SigningLimits.MaxFieldValueLength);
    }

    /// <summary>
    /// 列上限与服务校验取同一个常量 —— 两处各写一个数字就会漂。
    /// SQLite 不强制 varchar 宽度，所以列越界本身在这套测试里看不见；钉住列宽是唯一的防线。
    /// </summary>
    [Fact]
    public void The_column_limits_match_the_service_limits()
    {
        var signer = DbContext.Model.FindEntityType(typeof(Signer))!;
        signer.FindProperty(nameof(Signer.SignatureImage))!.GetMaxLength().ShouldBe(SigningLimits.MaxSignatureImageLength);
        signer.FindProperty(nameof(Signer.ConsentText))!.GetMaxLength().ShouldBe(SigningLimits.MaxConsentTextLength);
        signer.FindProperty(nameof(Signer.SignerUserAgent))!.GetMaxLength().ShouldBe(SigningLimits.MaxSignerUserAgentLength);
        signer.FindProperty(nameof(Signer.DeclineReason))!.GetMaxLength().ShouldBe(SigningLimits.MaxDeclineReasonLength);
        signer.FindProperty(nameof(Signer.Name))!.GetMaxLength().ShouldBe(SigningLimits.MaxSignerNameLength);
        signer.FindProperty(nameof(Signer.Role))!.GetMaxLength().ShouldBe(SigningLimits.MaxRoleLength);
        signer.FindProperty(nameof(Signer.Email))!.GetMaxLength().ShouldBe(SigningLimits.MaxEmailLength);

        var value = DbContext.Model.FindEntityType(typeof(FieldValue))!;
        value.FindProperty(nameof(FieldValue.Value))!.GetMaxLength().ShouldBe(SigningLimits.MaxFieldValueLength);
        value.FindProperty(nameof(FieldValue.FieldKey))!.GetMaxLength().ShouldBe(SigningLimits.MaxFieldKeyLength);

        // 发起方写入的列（CreateAsync 在落库之前按同一组常量校验）。
        var envelope = DbContext.Model.FindEntityType(typeof(Envelope))!;
        envelope.FindProperty(nameof(Envelope.Title))!.GetMaxLength().ShouldBe(SigningLimits.MaxTitleLength);
        envelope.FindProperty(nameof(Envelope.HostEntityType))!.GetMaxLength().ShouldBe(SigningLimits.MaxHostEntityTypeLength);
    }
}
