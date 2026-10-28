using System.Diagnostics.CodeAnalysis;

namespace Tnzi.Security;

/// <summary>
/// 把 JSON 载荷里的敏感字段值换成掩码。无状态、线程安全，可单例注册。
/// </summary>
/// <remarks>
/// ★★★ <strong>它住在核心程序集，是因为它有两个互不相识的消费方。</strong>
/// 原先它在 <c>Tnzi.Audit</c> 里，于是 <c>Tnzi.AspNetCore</c> 的
/// <c>RequestTrackingMiddleware</c> 引用不到它（依赖方向是 Audit → AspNetCore）——
/// 后果不是「少了一个工具类」，而是那个中间件的 <c>LogRequestBody</c> /
/// <c>LogResponseBody</c> <b>完全不脱敏</b>：查询串里的 <c>password</c> 被抹掉，
/// 而请求体里的密码原样进日志，登录响应里的访问令牌与刷新令牌也是。
/// 两个开关都带 <c>[RuntimeSetting]</c>，在配置中心里点一下就能打开。
/// </remarks>
public class RequestBodyRedactor
{
    /// <summary>
    /// 敏感值统一掩码。请求体脱敏与实体级审计的属性值脱敏
    /// （EntityAuditSaveChangesInterceptor）共用，保证审计数据脱敏语义一致。
    /// </summary>
    public const string RedactedValue = "***REDACTED***";

    /// <summary>
    /// 默认敏感字段名（不区分大小写，精确匹配）。
    /// </summary>
    /// <remarks>
    /// ★ 单一真值源：审计模块的 <c>AuditOptions.SensitiveFields</c> 与请求日志共用这一份。
    /// 各写一份的结果是「某个流程比别处多露出一个字段」—— 不报错，也不会让测试变红。
    /// ★★★ 名单按属性名<b>精确</b>匹配、且<b>跨实体生效</b>，所以只能收录不会误伤业务字段的名字。
    /// <c>code</c> 尤其进不来：加它会把全仓所有科目代码、货币代码、模块编码在审计里一起掩掉，
    /// 换来的只是「验证码不进审计」—— 而那件事该由 <c>TwoFactorCode</c> 上的
    /// <c>[AuditIgnore]</c> 精确解决。<c>value</c> 同理（<c>AuthToken.Value</c> 走属性级豁免）。
    /// 认证端点的请求体另有一层保护：<c>RequestTrackingMiddleware</c> 对 <c>auth/</c> 下的路径
    /// 整条不采集 body，与开关无关。
    /// ★ 框架自己端点上的凭据名必须在这里：TOTP 绑定响应的 <c>sharedKey</c> / <c>authenticatorUri</c>、
    /// MCP 服务器注册的 <c>authToken</c>、匿名推送注册响应的 <c>deviceKey</c>、支付响应的 <c>clientSecret</c>、
    /// 分享链接的 <c>shareToken</c>、滑块验证的 <c>passToken</c>。漏一个，打开体日志后它就原样进日志与审计表。
    /// </remarks>
    public static IReadOnlySet<string> DefaultSensitiveFields { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "password",
            "newPassword",
            "currentPassword",
            "confirmPassword",
            "passwordHash",
            "securityStamp",
            "token",
            "tempToken",
            "secret",
            "credential",
            "authorization",
            "accessToken",
            "refreshToken",
            "apiKey",
            // 支付渠道的 client secret（Stripe PaymentIntent / SetupIntent）与 OAuth 客户端密钥：
            // 表单编码分支另收了 client_secret，JSON 分支此前漏了驼峰形态
            "clientSecret",
            // 匿名推送设备密钥：明文只在签发响应里出现一次，库里只留哈希，拿到它就能冒充那台设备重注册推送地址
            "deviceKey",
            "connectionString",
            "creditCard",
            "sharedKey",
            "authenticatorUri",
            "authToken",
            "privateKey",
            "secretKey",
            "setPasswordToken",
            "shareToken",
            "passToken"
        };

    /// <summary>
    /// 体本身就是秘密材料、字段名认不出来的端点路径片段（不区分大小写，按 <c>/片段/</c> 包含匹配）。
    /// </summary>
    /// <remarks>
    /// 加密设置的写入体是 <c>{"value": 明文}</c>，<c>value</c> 进不了字段名单（会把全仓的业务值一起掩掉）；
    /// 第二因子端点上的验证码叫 <c>code</c>，同理。这些端点按路径整条不采集体，请求日志、异常诊断与审计同一口径。
    /// </remarks>
    private static readonly string[] SecretBodyPathSegments =
    [
        "/two-factor/", "/encrypted/"
    ];

    /// <summary>
    /// 这条请求的体（请求体与响应体）是否属于秘密材料、一律不该被采集。
    /// </summary>
    /// <param name="path">请求路径（<c>HttpRequest.Path</c>）；空时返回 <c>false</c>。</param>
    public static bool IsSecretBodyPath(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        // 尾部补一个 '/'，让以片段结尾的路径（".../encrypted"）也命中 "/encrypted/"
        var probe = path.EndsWith('/') ? path : path + "/";
        foreach (var segment in SecretBodyPathSegments)
        {
            if (probe.Contains(segment, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Redact sensitive field values in a JSON string.
    /// Returns the original string if it is not valid JSON.
    /// </summary>
    /// <remarks>
    /// ★ 非法 JSON 原样返回，所以调用方<b>必须先脱敏、后截断</b>：截断过的 JSON 是非法串，
    /// 先截断再交给它等于把凭据原样放过。要落库（任何读得到那张表的人都能看见）的场景用
    /// <see cref="TryRedact"/>，脱敏不了就不存。
    /// </remarks>
    /// <param name="json">Raw JSON request body</param>
    /// <param name="sensitiveFields">Set of field names to redact (case-insensitive)</param>
    /// <returns>JSON string with sensitive values replaced</returns>
    public string Redact(string json, IReadOnlySet<string> sensitiveFields)
        // Not valid JSON, return as-is
        => TryRedact(json, sensitiveFields, out var redacted) ? redacted : json;

    /// <summary>
    /// 失败关闭的脱敏：只有整段是合法 JSON 时才返回 <c>true</c> 并给出脱敏结果。
    /// </summary>
    /// <param name="json">完整的 JSON 文本（不得是截断过的前缀）</param>
    /// <param name="sensitiveFields">要脱敏的字段名（不区分大小写）；为空时合法 JSON 原样返回</param>
    /// <param name="redacted">成功时为脱敏后的文本；返回 <c>false</c> 时为 <c>null</c>，绝不回传原文</param>
    /// <returns>载荷是合法 JSON 时为 <c>true</c>；不是（包括被截断的 JSON）时为 <c>false</c>，名单为空也一样。</returns>
    /// <remarks>
    /// ★ 与 <see cref="Redact"/> 的区别只在失败时：<see cref="Redact"/> 对非法 JSON <b>原样返回</b>，
    /// 调用方分不清「没有敏感字段」与「根本没脱敏」。把文本写进持久存储的调用方用这个，失败时自己决定记什么
    /// （通常是一个标记）：一段被截断的 JSON 就是非法 JSON，原样存下等于把其中的口令存下。
    /// </remarks>
    public bool TryRedact(string json, IReadOnlySet<string> sensitiveFields, [NotNullWhen(true)] out string? redacted)
    {
        Check.NotNull(json);
        Check.NotNull(sensitiveFields);

        try
        {
            using var document = JsonDocument.Parse(json);
            if (sensitiveFields.Count == 0)
            {
                redacted = json;
                return true;
            }

            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
            {
                RedactElement(writer, document.RootElement, sensitiveFields);
            }

            redacted = Encoding.UTF8.GetString(stream.ToArray());
            return true;
        }
        catch (JsonException)
        {
            redacted = null;
            return false;
        }
    }

    private static void RedactElement(Utf8JsonWriter writer, JsonElement element, IReadOnlySet<string> sensitiveFields)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name);
                    if (IsSensitiveField(property.Name, sensitiveFields))
                    {
                        writer.WriteStringValue(RedactedValue);
                    }
                    else
                    {
                        RedactElement(writer, property.Value, sensitiveFields);
                    }
                }
                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    RedactElement(writer, item, sensitiveFields);
                }
                writer.WriteEndArray();
                break;

            default:
                element.WriteTo(writer);
                break;
        }
    }

    /// <summary>
    /// 字段名精确命中名单即敏感；以配置路径为键（<c>Stripe:SecretKey</c>，配置中心的保存体就是这种形态）时
    /// 按最后一段判定。业务 JSON 的属性名不含冒号，所以这条规则不会扩大对业务字段的误伤面。
    /// </summary>
    private static bool IsSensitiveField(string fieldName, IReadOnlySet<string> sensitiveFields)
    {
        if (sensitiveFields.Contains(fieldName))
        {
            return true;
        }

        var lastSeparator = fieldName.LastIndexOf(':');
        return lastSeparator >= 0
               && lastSeparator < fieldName.Length - 1
               && sensitiveFields.Contains(fieldName[(lastSeparator + 1)..]);
    }
}
