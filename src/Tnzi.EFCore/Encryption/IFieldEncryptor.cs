namespace Tnzi.EFCore.Encryption;

/// <summary>
/// 字段级加密器：把敏感列的明文封成密文再落库，读取时还原。
/// </summary>
/// <remarks>
/// <para>
/// <strong>为什么是同步接口。</strong>EF Core 的 <c>ValueConverter</c> 只能同步执行，
/// 因此本接口不能是异步的。这不妨碍对接 KMS 一类的远端密钥服务：信封加密的通行做法是
/// 远端只负责封装/解封<em>数据密钥</em>，实现方把解封后的数据密钥缓存在内存里，
/// 每行数据的加解密都在本地同步完成。实现方若必须在此方法内做远端调用，
/// 需自行保证不会在同步上下文里死锁。
/// </para>
/// <para>
/// <strong>purpose 参数是安全边界，不是日志标签。</strong>它标识密文属于哪个实体的哪个属性
/// （形如 <c>Tnzi.Identity.User.IdCardNumber</c>），实现方 MUST 把它绑定进密文
/// （AES-GCM 用附加认证数据 AAD 即可，核心的 <c>AesGcmHelper</c> 已支持）。
/// 否则攻击者可以把 A 列的密文整段复制到 B 列，数据库看不出异常，解密也能成功，
/// 即所谓密文重放。
/// </para>
/// <para>
/// <strong>可选能力。</strong>框架不会自动注册任何实现，也不会为未使用者引入任何开销。
/// 只有在实体配置里对某个属性调用了 <c>IsEncrypted()</c> 之后，才需要一个实现存在。
/// </para>
/// </remarks>
[ExperimentalApi(Reason = "字段级加密的密钥轮换与加密删除仍在演进，0.2 前可能调整密文前缀布局")]
public interface IFieldEncryptor
{
    /// <summary>
    /// 加密明文。
    /// </summary>
    /// <param name="plaintext">明文。</param>
    /// <param name="purpose">用途标识，形如 <c>Namespace.Entity.Property</c>，MUST 参与认证。</param>
    /// <returns>自描述的密文串（含密钥标识与格式版本，可直接落库）。</returns>
    string Encrypt(string plaintext, string purpose);

    /// <summary>
    /// 解密密文。
    /// </summary>
    /// <param name="ciphertext">由 <see cref="Encrypt"/> 产生的密文串。</param>
    /// <param name="purpose">与加密时相同的用途标识，不一致 MUST 解密失败。</param>
    /// <returns>明文。</returns>
    /// <exception cref="FieldEncryptionException">
    /// 密文损坏、被篡改、用途不匹配，或所需密钥已不在密钥环中（例如已被加密删除）。
    /// </exception>
    string Decrypt(string ciphertext, string purpose);

    /// <summary>
    /// 判断一个已存储的值是否已是本加密器产出的密文。
    /// </summary>
    /// <remarks>
    /// 供「给既有明文列加密」的迁移场景使用：迁移期间同一列会同时存在明文与密文，
    /// 读取路径据此决定是否需要解密。迁移完成后该判断恒为真。
    /// </remarks>
    bool IsEncrypted(string value);
}
