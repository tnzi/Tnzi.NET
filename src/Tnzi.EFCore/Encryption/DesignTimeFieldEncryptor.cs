namespace Tnzi.EFCore.Encryption;

/// <summary>
/// 设计期（<c>dotnet ef</c>）建模用的占位加密器：只让 <c>IsEncrypted()</c> 建出与运行期相同的模型，任何一次真正的转换都抛异常。
/// </summary>
/// <remarks>
/// 设计期工厂建上下文时没有应用容器，取不到真正的加密器；而迁移只需要模型（列类型、放宽后的列长），不需要加解密。
/// 刻意不做任何转换：设计期若真的要落一个加密列的值（例如种子数据），产出明文是这项能力最危险的失败方式，
/// 宁可让命令失败。运行期取不到加密器仍由 <c>IsEncrypted()</c> 直接拒绝。
/// </remarks>
internal sealed class DesignTimeFieldEncryptor : IFieldEncryptor
{
    public static readonly DesignTimeFieldEncryptor Instance = new();

    private DesignTimeFieldEncryptor()
    {
    }

    public string Encrypt(string plaintext, string purpose) => throw NotAvailable();

    public string Decrypt(string ciphertext, string purpose) => throw NotAvailable();

    public bool IsEncrypted(string value) => throw NotAvailable();

    public int CiphertextOverheadChars => FieldEncryptionDesignTime.Mode == FieldEncryptionMode.Envelope
        ? EnvelopeFieldEncryptor.OverheadChars
        : FieldEncryptionOptions.MaxKeyIdLength + 4;

    private static InvalidOperationException NotAvailable() => new(
        "Field encryption is not available at design time. A design-time command tried to convert a value of a property "
        + "configured with IsEncrypted() (for example seed data); encrypted values cannot be produced without the application's key ring.");
}
