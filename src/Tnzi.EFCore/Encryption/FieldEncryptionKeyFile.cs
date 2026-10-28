using System.Text.Json.Serialization;

namespace Tnzi.EFCore.Encryption;

/// <summary>
/// 字段加密主密钥文件(<c>EFCore:FieldEncryption:KeyFile</c> 指向的 JSON 文件)的解析。
/// </summary>
/// <remarks>
/// <para>
/// 文件格式(UTF-8 JSON,允许注释与尾逗号,不认识的属性一律报错以免拼错的字段被静默忽略):
/// <code>
/// {
///   "activeKeyId": "2026-10",
///   "keys": {
///     "2026-10": "&lt;Base64 编码的 256 位密钥&gt;",
///     "2026-01": "&lt;旧密钥,仅供解密存量数据&gt;"
///   }
/// }
/// </code>
/// <c>activeKeyId</c> 可省略(此时由配置里的 <c>ActiveKeyId</c> 指定)。密钥标识与密钥材料的规则与配置密钥环相同。
/// </para>
/// <para>
/// 文件应放在数据库与随机器同步的配置之外,只给应用进程读权限(容器里就是一个挂载的 secret 文件)。
/// 文件内容变化后无需重启:密钥环会重新读取(见 <see cref="FieldEncryptionKeyRing"/>)。
/// </para>
/// </remarks>
internal sealed class FieldEncryptionKeyFile
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    /// <summary>文件里声明的活动密钥标识,可为空。</summary>
    public string? ActiveKeyId { get; init; }

    /// <summary>密钥标识 → Base64 编码的 256 位密钥。</summary>
    public IReadOnlyDictionary<string, string> Keys { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// 读取并校验密钥文件。
    /// </summary>
    /// <param name="path">密钥文件路径。</param>
    /// <param name="file">成功时的解析结果。</param>
    /// <param name="errors">失败原因(英文,不含密钥材料)。</param>
    public static bool TryLoad(string path, out FieldEncryptionKeyFile file, out List<string> errors)
    {
        file = new FieldEncryptionKeyFile();
        errors = [];

        string json;
        try
        {
            json = File.ReadAllText(path, Encoding.UTF8);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            errors.Add($"key file '{path}' cannot be read: {ex.Message}");
            return false;
        }

        RawFile? raw;
        try
        {
            raw = JsonSerializer.Deserialize<RawFile>(json, SerializerOptions);
        }
        catch (JsonException ex)
        {
            errors.Add($"key file '{path}' is not valid: {ex.Message} Expected {{ \"activeKeyId\": \"...\", \"keys\": {{ \"<id>\": \"<base64 256-bit key>\" }} }}.");
            return false;
        }

        if (raw?.Keys is not { Count: > 0 })
        {
            errors.Add($"key file '{path}' contains no keys. Expected {{ \"activeKeyId\": \"...\", \"keys\": {{ \"<id>\": \"<base64 256-bit key>\" }} }}.");
            return false;
        }

        var keyErrors = new List<string>();
        FieldEncryptionKeyValidation.ValidateKeys(raw.Keys, keyErrors.Add);
        if (keyErrors.Count > 0)
        {
            errors.AddRange(keyErrors.Select(message => $"key file '{path}': {message}"));
            return false;
        }

        file = new FieldEncryptionKeyFile { ActiveKeyId = raw.ActiveKeyId, Keys = raw.Keys };
        return true;
    }

    private sealed class RawFile
    {
        [JsonPropertyName("activeKeyId")]
        public string? ActiveKeyId { get; set; }

        [JsonPropertyName("keys")]
        public Dictionary<string, string>? Keys { get; set; }
    }
}
