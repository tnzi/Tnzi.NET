using System.Text;

namespace Tnzi.Finance.Tests;

/// <summary>
/// EFT 定宽记录的度量单位必须是<b>文件真正写出的那个单位</b>。
/// </summary>
/// <remarks>
/// NACHA 与 CPA-005 都是 ASCII 定位格式，接收行按字节偏移解析；而写入器此前按 UTF-16 字符数
/// 补齐与校验、文件却按 UTF-8 输出 —— 一个「é」占 1 个 char / 2 个字节，收款人叫
/// "Café Bélanger" 的那条记录就比规定宽两个字节，其后每个字段全部错位，装批 / 生成 / 下载
/// 每一步 200，失败发生在银行侧且与真正原因无关。加拿大格式里法语重音名是常态不是边角。
/// 判据与「截断哪一端」那条同族：<b>度量哪个单位</b>。
/// </remarks>
public class EftFieldEncodingTests
{
    private static EftComposeRequest Cpa005Request() => new()
    {
        Format = EftFileFormat.Cpa005,
        Currency = "CAD",
        EffectiveDate = new DateTime(2026, 7, 20),
        CreationTime = new DateTime(2026, 7, 13, 10, 30, 0),
        FileCreationNumber = 1,
        OriginatorId = "CPA0012345",
        OriginatorName = "Société Générale des Épiceries",
        OriginatorInstitutionNumber = "001",
        OriginatorTransitNumber = "12345",
        OriginatorAccountNumber = "111222333",
        Entries =
        {
            new EftComposeEntry { PayeeName = "Café Bélanger Inc.", InstitutionNumber = "002", TransitNumber = "54321", AccountNumber = "1234567", AccountType = BankAccountType.Checking, Amount = 100.00m }
        }
    };

    private static EftComposeRequest NachaRequest() => new()
    {
        Format = EftFileFormat.Nacha,
        Currency = "USD",
        EffectiveDate = new DateTime(2026, 7, 20),
        CreationTime = new DateTime(2026, 7, 13, 10, 30, 0),
        FileCreationNumber = 1,
        OriginatorId = "123456789",
        OriginatorName = "Zoë & Søren LLC",
        BankName = "FIRST BANK",
        OriginatorRoutingNumber = "021000021",
        OriginatorAccountNumber = "111222333",
        Entries =
        {
            new EftComposeEntry { PayeeName = "José Muñoz", RoutingNumber = "011401533", AccountNumber = "1234567", AccountType = BankAccountType.Checking, Amount = 100.00m }
        }
    };

    [Fact]
    public void Text_FoldsAccentedLettersToAscii_AndKeepsTheWidthInBytes()
    {
        var field = EftFieldWriter.Text("Café Bélanger", 30);

        field.ShouldBe("Cafe Belanger".PadRight(30));
        Encoding.UTF8.GetByteCount(field).ShouldBe(30);
    }

    /// <summary>没有 ASCII 等价物的字符（不可分解的字母、emoji）换成 '?'，宽度仍以字节计。</summary>
    [Fact]
    public void Text_ReplacesCharactersWithoutAnAsciiEquivalent()
    {
        var field = EftFieldWriter.Text("Łódź 🏦", 12);

        field.ShouldBe("?odz ?".PadRight(12));
        Encoding.UTF8.GetByteCount(field).ShouldBe(12);
    }

    /// <summary>不变量必须度量文件真正写出的单位：一条 char 数正好、字节数越界的记录要被抓住。</summary>
    [Fact]
    public void Fixed_RejectsARecordWhoseByteLengthDiffersFromItsCharLength()
    {
        var record = "é" + new string(' ', 239); // 240 chars, 241 UTF-8 bytes

        Should.Throw<BusinessException>(() => EftFieldWriter.Fixed(record, 240));
    }

    /// <summary>账号里的非 ASCII 不能折叠成另一个账号：拒绝，与超长账号同一类处理。</summary>
    [Fact]
    public void AccountField_RejectsNonAsciiInsteadOfFolding()
    {
        Should.Throw<BusinessException>(() => EftFieldWriter.AccountField("12345é7", 17, "payee 'X'"))
            .Message.ShouldContain("payee 'X'");
    }

    [Fact]
    public void Cpa005_WithAccentedNames_EveryRecordIs1464Bytes()
    {
        var result = new DefaultEftFileComposer().Compose(Cpa005Request());
        result.Succeeded.ShouldBeTrue(result.Message);

        var lines = result.Data!.Content.Split('\n');
        lines.All(l => Encoding.UTF8.GetByteCount(l) == 1464).ShouldBeTrue("every CPA-005 record must be 1464 bytes, not 1464 chars");
        lines.All(l => l.All(c => c <= 0x7F)).ShouldBeTrue("the file must be pure ASCII");
        result.Data.Content.ShouldContain("Cafe Belanger Inc.");
        result.Data.Content.ShouldContain("Societe Generale");
    }

    [Fact]
    public void Nacha_WithAccentedNames_EveryRecordIs94Bytes()
    {
        var result = new DefaultEftFileComposer().Compose(NachaRequest());
        result.Succeeded.ShouldBeTrue(result.Message);

        var lines = result.Data!.Content.Split('\n');
        lines.All(l => Encoding.UTF8.GetByteCount(l) == 94).ShouldBeTrue("every NACHA record must be 94 bytes, not 94 chars");
        lines.All(l => l.All(c => c <= 0x7F)).ShouldBeTrue("the file must be pure ASCII");
        result.Data.Content.ShouldContain("Jose Munoz");
        result.Data.Content.ShouldContain("Zoe & S?ren LLC");
    }
}
