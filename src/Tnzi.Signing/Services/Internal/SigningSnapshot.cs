using System.Text.Json;

namespace Tnzi.Signing.Services.Internal;

/// <summary>
/// 请求发出那一刻，模板与其字段的冻结副本。
/// </summary>
/// <remarks>
/// <para>
/// ★ <b>权威内容是这份快照，不是活的模板。</b>一份已经发出去的文档，其页面内容与每个签名框的
/// 落点都必须钉死：中途有人改了模板，正在看这份文件的签署人不该因此看到不同的东西，
/// 已经收集到的签名更不该因为框挪了位置而失去意义。
/// </para>
/// <para>
/// 存 JSON 而不是外键指向字段行，正是为了让"模板后来改了/删了"与这份文档彻底无关。
/// </para>
/// </remarks>
public sealed record SigningSnapshot
{
    /// <summary>来源模板 id（仅供追溯）</summary>
    public Guid? TemplateId { get; init; }

    /// <summary>来源模板版本（快照生成时的 <c>EnvelopeTemplate.Version</c>）</summary>
    public int TemplateVersion { get; init; }

    /// <summary>模板名</summary>
    public string TemplateName { get; init; } = string.Empty;

    /// <summary>冻结的字段</summary>
    public IReadOnlyList<SnapshotField> Fields { get; init; } = [];

    private static readonly JsonSerializerOptions SerializerOptions = new(TnziJsonDefaults.Options);

    public string ToJson() => JsonSerializer.Serialize(this, SerializerOptions);

    /// <summary>
    /// 解析快照。解析不出时返回 <c>null</c> —— 调用方必须当作"这份请求无法处理"，
    /// 而不是当作"没有字段"：后者会把一份该拦下的文档安静地密封成一张白纸。
    /// </summary>
    public static SigningSnapshot? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<SigningSnapshot>(json, SerializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>快照里的一个字段。</summary>
public sealed record SnapshotField
{
    public string Key { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public SigningFieldType Type { get; init; }
    public string? RecipientRole { get; init; }
    public string? Binding { get; init; }
    public bool Required { get; init; }
    public FieldPlacementMode PlacementMode { get; init; }
    public string? AnchorText { get; init; }
    public int Page { get; init; } = 1;
    public decimal X { get; init; }
    public decimal Y { get; init; }
    public decimal W { get; init; }
    public decimal H { get; init; }
    public decimal? FontSize { get; init; }

    /// <summary>这个字段是不是要落一张图（签名 / 缩写）。</summary>
    public bool IsSignatureLike => Type is SigningFieldType.Signature or SigningFieldType.Initials;

    public static SnapshotField From(Field field) => new()
    {
        Key = field.Key,
        Label = field.Label,
        Type = field.Type,
        RecipientRole = field.RecipientRole,
        Binding = field.Binding,
        Required = field.Required,
        PlacementMode = field.PlacementMode,
        AnchorText = field.AnchorText,
        Page = field.Page,
        X = field.X,
        Y = field.Y,
        W = field.W,
        H = field.H,
        FontSize = field.FontSize,
    };
}
