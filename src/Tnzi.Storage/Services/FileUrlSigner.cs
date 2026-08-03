namespace Tnzi.Storage.Services;

/// <summary>
/// <see cref="IFileUrlSigner"/> 的默认实现:HMAC-SHA256 + Base64Url。
///
/// 令牌形如 <c>1.{exp}.{uid}.{sig}</c>:
/// <list type="bullet">
/// <item><c>1</c> —— 版本前缀,预留密钥/算法轮换(与 <c>AesGcmHelper</c> 的 v1 前缀同一考虑)</item>
/// <item><c>exp</c> —— Unix 秒</item>
/// <item><c>uid</c> —— 签发对象的 GUID(N 格式)或空,只为审计</item>
/// <item><c>sig</c> —— <c>HMACSHA256(key, "{fileId:N}|{exp}|{uid}")</c></item>
/// </list>
///
/// **fileId 进签名载荷但不进令牌本身**:它已经在 URL 路径里了,重复携带只是徒增长度;
/// 参与签名则保证了"A 文件的令牌换不到 B 文件"。
///
/// 单例:密钥在构造时解析一次。
/// </summary>
public class FileUrlSigner : IFileUrlSigner
{
    private const string Version = "1";

    private readonly byte[] _key;

    public FileUrlSigner(
        IOptions<StorageOptions> options,
        IConfiguration configuration,
        ILogger<FileUrlSigner> logger)
    {
        Check.NotNull(options);
        Check.NotNull(configuration);
        Check.NotNull(logger);

        _key = ResolveKey(options.Value, configuration, logger);
    }

    /// <summary>
    /// 密钥来源,依次回退:
    /// <c>Storage:UrlSigningKey</c> → <c>Identity:Jwt:SecretKey</c> → 进程内随机。
    ///
    /// 回退到 JWT 密钥是因为它已经是"全部实例共享、部署时必配"的机密,复用它能让
    /// 绝大多数部署零配置就正确工作;签名令牌只活几分钟,与 JWT 共享密钥不放大风险。
    /// 两者都没有时只能进程内随机 —— 单实例可用,多实例会互相不认账,故记 Warning
    /// 而不是静默:静默的结果是"图片有时候能看有时候不能",最难查的那种。
    /// </summary>
    private static byte[] ResolveKey(StorageOptions options, IConfiguration configuration, ILogger logger)
    {
        if (!string.IsNullOrWhiteSpace(options.UrlSigningKey))
            return Encoding.UTF8.GetBytes(options.UrlSigningKey);

        var jwtKey = configuration["Identity:Jwt:SecretKey"];
        if (!string.IsNullOrWhiteSpace(jwtKey))
        {
            logger.LogInformation(
                "Storage:UrlSigningKey is not configured; deriving file access token signing from Identity:Jwt:SecretKey.");
            // 派生而不是直接复用同一把字节:同一密钥用于两种用途时,一处的实现缺陷
            // 会牵连另一处。域分隔字符串把它们隔开。
            return HMACSHA256.HashData(Encoding.UTF8.GetBytes(jwtKey), "Tnzi.Storage.FileUrlSigner.v1"u8.ToArray());
        }

        logger.LogWarning(
            "Neither Storage:UrlSigningKey nor Identity:Jwt:SecretKey is configured. File access tokens are signed with a per-process random key: they will stop working after a restart and will not validate across instances. Configure Storage:UrlSigningKey for any multi-instance deployment.");
        return RandomNumberGenerator.GetBytes(32);
    }

    public string Sign(Guid fileId, DateTimeOffset expiresAt, Guid? userId)
    {
        var exp = expiresAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var uid = userId.HasValue && userId.Value != Guid.Empty ? userId.Value.ToString("N") : string.Empty;
        var signature = ComputeSignature(fileId, exp, uid);
        return string.Concat(Version, ".", exp, ".", uid, ".", signature);
    }

    public bool TryValidate(Guid fileId, string? token, out Guid? userId)
    {
        userId = null;

        if (string.IsNullOrWhiteSpace(token))
            return false;

        // 4 段定长:版本 / 过期 / 用户 / 签名。用户段可以为空但分隔符必须在。
        var parts = token.Split('.');
        if (parts.Length != 4 || parts[0] != Version)
            return false;

        if (!long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var exp))
            return false;

        // 先验签再看过期:反过来会让攻击者用"是否报过期"区分签名对错。
        var expected = ComputeSignature(fileId, parts[1], parts[2]);
        if (!FixedTimeEquals(expected, parts[3]))
            return false;

        if (DateTimeOffset.FromUnixTimeSeconds(exp) <= DateTimeOffset.UtcNow)
            return false;

        if (!string.IsNullOrEmpty(parts[2]) && Guid.TryParseExact(parts[2], "N", out var parsed))
            userId = parsed;

        return true;
    }

    private string ComputeSignature(Guid fileId, string exp, string uid)
    {
        var payload = string.Concat(fileId.ToString("N"), "|", exp, "|", uid);
        var hash = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(payload));
        // BCL 的 Base64Url（.NET 9+）产出的就是无填充的 URL 安全编码，与此前手写的
        // ToBase64String + TrimEnd('=') + '+'→'-' + '/'→'_' 完全等价，不改变既有令牌格式。
        return Base64Url.EncodeToString(hash);
    }

    /// <summary>
    /// 定时比较。长度不等直接返回 false 是安全的:长度本就是公开信息(签名定长)。
    /// </summary>
    private static bool FixedTimeEquals(string expected, string actual)
    {
        var a = Encoding.UTF8.GetBytes(expected);
        var b = Encoding.UTF8.GetBytes(actual);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }
}
