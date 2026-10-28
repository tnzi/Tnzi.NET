namespace Tnzi.EFCore.Encryption;

/// <summary>
/// 把已存储的密文换到当前活动主密钥下的能力(主密钥轮换后的存量迁移)。
/// </summary>
/// <remarks>
/// <para>
/// 信封加密下,轮换主密钥不必重新加密载荷:只把每个值的数据密钥用旧主密钥解封、再用新主密钥封装,
/// 载荷字节原样保留。迁移期间新旧密文可以并存,读取始终可用。
/// </para>
/// <para>
/// 用法:取出加密列的<strong>原始存储值</strong>(值转换器会把它解成明文,所以要走不经转换器的 SQL / Dapper),
/// 对每个值 <see cref="NeedsRewrap"/> 为真就写回 <see cref="Rewrap"/> 的结果,用途标识与列上声明的一致。
/// 全部换完、确认没有存量密文还引用旧密钥之后,再把旧密钥从密钥环移除。
/// </para>
/// </remarks>
[ExperimentalApi(Reason = "字段级加密的信封格式与重新封装流程仍在演进,0.2 前可能调整")]
public interface IFieldRewrapper
{
    /// <summary>
    /// 这个已存储的值是否需要重新封装:是本加密器的密文,且封装它的不是当前活动密钥(或仍是旧的直接格式)。
    /// 明文与不认识的值返回 <c>false</c>。
    /// </summary>
    bool NeedsRewrap(string ciphertext);

    /// <summary>
    /// 返回在当前活动主密钥下的等价密文;已在活动密钥下的值原样返回。
    /// </summary>
    /// <param name="ciphertext">已存储的密文。</param>
    /// <param name="purpose">与加密时相同的用途标识,不一致 MUST 失败。</param>
    /// <exception cref="FieldEncryptionException">密文损坏、用途不匹配,或封装它的旧主密钥已不在密钥环里。</exception>
    string Rewrap(string ciphertext, string purpose);
}
