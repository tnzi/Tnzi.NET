namespace Tnzi.EFCore.Encryption;

/// <summary>
/// 字段级加密选项（配置节 <c>EFCore:FieldEncryption</c>）。
/// </summary>
/// <remarks>
/// <para>
/// <strong>默认关闭。</strong>未在配置里启用时，框架不注册任何加密器，
/// 也不做任何模型扫描，对不使用该能力的应用零影响。
/// </para>
/// <para>
/// <strong>密钥环而不是单把密钥。</strong>加密恒用 <see cref="ActiveKeyId"/> 指向的那把，
/// 解密则按密文里携带的密钥标识回查。轮换的做法是：往 <see cref="Keys"/> 里加一把新的、
/// 把 <see cref="ActiveKeyId"/> 指向它，旧密钥继续留在环里供存量密文解密。
/// </para>
/// <para>
/// <strong>从密钥环里移除一把密钥，等于把用它加密的所有数据永久变为不可读。</strong>
/// 这既是误删的后果，也是「加密删除」的实现手段：要销毁一批数据而又无法证明
/// 备份介质上的副本已被覆盖时，销毁密钥是唯一能给出确定答案的做法。
/// </para>
/// </remarks>
[ConfigSection("EFCore:FieldEncryption")]
public class FieldEncryptionOptions
{
    /// <summary>
    /// 是否启用字段级加密。默认 <c>false</c>。
    /// </summary>
    /// <remarks>
    /// 为 <c>false</c> 时，实体配置里若仍调用了 <c>IsEncrypted()</c>，
    /// 会在模型构建期抛出异常而不是静默明文落库，避免「以为加了密其实没有」。
    /// </remarks>
    public bool Enabled { get; set; }

    /// <summary>
    /// 当前用于加密的密钥标识，必须是 <see cref="Keys"/> 中的一个键。
    /// </summary>
    public string? ActiveKeyId { get; set; }

    /// <summary>
    /// 密钥环：密钥标识 → Base64 编码的 256 位（32 字节）密钥。
    /// </summary>
    /// <remarks>
    /// 密钥标识只允许字母、数字、连字符与下划线（会作为密文前缀的一部分）。
    /// 密钥材料本身 MUST 来自配置提供程序中的机密源（环境变量 / 云端机密管理），
    /// 不要写进随代码提交的 appsettings.json。
    /// </remarks>
    public Dictionary<string, string> Keys { get; set; } = [];
}
