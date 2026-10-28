namespace Tnzi.AI.Services;

/// <summary>
/// 聊天请求带的每轮元数据（<see cref="ChatRequestDto.Metadata"/>）：校验、存档与读回。
/// </summary>
/// <remarks>
/// <para>
/// 元数据是消费方自己定义的一小组值，描述这一轮指向什么（例如用户这条消息点名的几台设备的 id）。框架不解释它：
/// 外部 CLI 路径把它交给 <see cref="ICliAgentTurnHandler"/>（<see cref="CliTurnContext.Metadata"/>），
/// 内建路径放在 <see cref="AgentRunRequest.TurnMetadata"/> 上供中间件读，两条路径都随用户消息存下。
/// </para>
/// <para>
/// 上限（键数、序列化后的字节数，<see cref="ChatMetadataOptions"/>）在服务层挡：它随每一轮存进线程、
/// 重新生成时原样再发一次，不设上限就是一个随每条消息无限增长的列。
/// </para>
/// </remarks>
public static class ChatTurnMetadata
{
    /// <summary>键的形状：字母开头，字母、数字、<c>_</c> <c>.</c> <c>:</c> <c>-</c>，至多 64 个字符。</summary>
    private static readonly Regex KeyPattern = new(
        "^[A-Za-z][A-Za-z0-9_.:-]{0,63}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    /// <summary>
    /// 校验一份元数据，返回可以交下去的副本（值已从请求的 JSON 文档里克隆出来）；没有元数据时返回空字典。
    /// </summary>
    /// <param name="metadata">请求带来的元数据。</param>
    /// <param name="limits">上限。</param>
    public static Result<Dictionary<string, JsonElement>> Validate(
        IReadOnlyDictionary<string, JsonElement>? metadata, ChatMetadataOptions limits)
    {
        Check.NotNull(limits);

        var copy = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (metadata is null || metadata.Count == 0)
        {
            return Result<Dictionary<string, JsonElement>>.Success(copy);
        }

        if (metadata.Count > limits.MaxKeys)
        {
            return Invalid($"Metadata can carry at most {limits.MaxKeys} keys; this request has {metadata.Count}.");
        }

        foreach (var (key, value) in metadata)
        {
            if (string.IsNullOrEmpty(key) || !KeyPattern.IsMatch(key))
            {
                return Invalid("Metadata keys start with a letter and contain only letters, digits, '_', '.', ':' or '-' " +
                               $"(at most 64 characters): '{Shorten(key)}' does not.");
            }

            if (value.ValueKind == JsonValueKind.Undefined)
            {
                return Invalid($"Metadata value of '{key}' is missing.");
            }

            copy[key] = value.Clone();
        }

        var size = Encoding.UTF8.GetByteCount(Serialize(copy)!);
        if (size > limits.MaxBytes)
        {
            return Invalid($"Metadata is {size} bytes; the limit is {limits.MaxBytes}.");
        }

        return Result<Dictionary<string, JsonElement>>.Success(copy);
    }

    /// <summary>存档形式（JSON 对象）；没有元数据时为 null。</summary>
    public static string? Serialize(IReadOnlyDictionary<string, JsonElement>? metadata)
        => metadata is { Count: > 0 } ? JsonSerializer.Serialize(metadata, TnziJsonDefaults.Options) : null;

    /// <summary>读回存档的元数据；没有或读不懂时为 null（元数据是附带信息，读不懂不该让整条消息读不出来）。</summary>
    public static Dictionary<string, JsonElement>? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, TnziJsonDefaults.Options);
            return parsed is { Count: > 0 } ? parsed : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Result<Dictionary<string, JsonElement>> Invalid(string message)
        => Result<Dictionary<string, JsonElement>>.Failure(message, 400, ErrorCodes.InvalidTurnMetadata);

    private static string Shorten(string? key)
        => key is null ? string.Empty : key.Length <= 40 ? key : key[..40] + "…";
}
