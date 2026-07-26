namespace Tnzi.Finance.Payroll.Services.Internal;

/// <summary>
/// 员工扩展属性（AttributesJson）解析与校验
/// </summary>
/// <remarks>
/// 存储形态为 JSON 对象、值限标量（字符串/数字/布尔）；
/// 解析结果为忽略大小写的字符串字典，供公式 Attr()/AttrText() 读取
/// （数字值以 invariant 文本保存进字典，Attr() 再按 invariant 解析回 decimal）。
/// </remarks>
public static class PayrollAttributeHelper
{
    /// <summary>
    /// 校验 AttributesJson 是否为合法的标量值 JSON 对象；null/空白视为合法（无属性）
    /// </summary>
    public static Result Validate(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Result.Success();

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return Result.Failure("Attributes must be a JSON object.", 400);

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                    return Result.Failure($"Attribute '{property.Name}' must be a scalar value (string, number or boolean).", 400);

                // 合法 JSON 数字可以超出 decimal 值域（如 1e400）：在这里挡住，
                // 否则它会一路存进库，直到计算工资单时 Parse 才炸
                if (property.Value.ValueKind == JsonValueKind.Number && !property.Value.TryGetDecimal(out _))
                    return Result.Failure($"Attribute '{property.Name}' is a number outside the supported range.", 400);
            }

            return Result.Success();
        }
        catch (JsonException)
        {
            return Result.Failure("Attributes must be valid JSON.", 400);
        }
    }

    /// <summary>
    /// 解析 AttributesJson 为忽略大小写的字符串字典（null/空白返回空字典；
    /// 调用前须已通过 <see cref="Validate"/>，非法输入抛 <see cref="JsonException"/>）
    /// </summary>
    public static IReadOnlyDictionary<string, string> Parse(string? json)
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json))
            return attributes;

        using var document = JsonDocument.Parse(json);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            attributes[property.Name] = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString() ?? string.Empty,
                // TryGetDecimal 而非 GetDecimal：超出 decimal 值域的数字原样带出去，
                // 由 Attr() 在求值时报"不是数字"，而不是在这里抛异常拖垮整批计算
                JsonValueKind.Number => property.Value.TryGetDecimal(out var number)
                    ? number.ToString(CultureInfo.InvariantCulture)
                    : property.Value.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => string.Empty
            };
        }

        return attributes;
    }
}
