
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
            "connectionString",
            "creditCard"
        };

    /// <summary>
    /// Redact sensitive field values in a JSON string.
    /// Returns the original string if it is not valid JSON.
    /// </summary>
    /// <param name="json">Raw JSON request body</param>
    /// <param name="sensitiveFields">Set of field names to redact (case-insensitive)</param>
    /// <returns>JSON string with sensitive values replaced</returns>
    public string Redact(string json, IReadOnlySet<string> sensitiveFields)
    {
        Check.NotNullOrWhiteSpace(json);
        Check.NotNull(sensitiveFields);

        if (sensitiveFields.Count == 0)
        {
            return json;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
            {
                RedactElement(writer, document.RootElement, sensitiveFields);
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (JsonException)
        {
            // Not valid JSON, return as-is
            return json;
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

    private static bool IsSensitiveField(string fieldName, IReadOnlySet<string> sensitiveFields)
    {
        return sensitiveFields.Contains(fieldName);
    }
}
