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
    /// 密钥标识的最大长度。标识写进每一条密文，<c>IsEncrypted()</c> 放宽列长时按这个上限预留。
    /// </summary>
    public const int MaxKeyIdLength = 64;

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

    /// <summary>
    /// 主密钥文件路径(JSON,格式见模块文档「字段级加密」)。可与 <see cref="Keys"/> 并用,二者的密钥标识不得重复。
    /// </summary>
    /// <remarks>
    /// 建议用绝对路径(相对路径按进程工作目录解析)。启用且文件缺失或格式错误时,应用启动失败并指出原因。
    /// 密钥材料不进数据库、不进随机器同步的配置,是这个来源存在的理由。
    /// </remarks>
    public string? KeyFile { get; set; }

    /// <summary>
    /// 加密形态。默认 <see cref="FieldEncryptionMode.Direct"/>:切到 <see cref="FieldEncryptionMode.Envelope"/> 会让密文变长,
    /// <c>IsEncrypted()</c> 放宽的列长随之变化,需要一次迁移。
    /// </summary>
    public FieldEncryptionMode Mode { get; set; } = FieldEncryptionMode.Direct;
}

/// <summary>
/// 字段加密的形态。
/// </summary>
public enum FieldEncryptionMode
{
    /// <summary>每个值直接用密钥环里的密钥加密,密文 <c>{keyId}:{v2:...}</c>。</summary>
    Direct = 0,

    /// <summary>
    /// 信封加密:每个值用一把新生成的随机数据密钥加密,数据密钥再被密钥环里的主密钥封装,封装后的数据密钥随密文存放,
    /// 密文 <c>{keyId}:e1:{封装的数据密钥}:{载荷}</c>。
    /// </summary>
    Envelope = 1
}
