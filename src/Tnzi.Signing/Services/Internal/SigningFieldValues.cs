namespace Tnzi.Signing.Services.Internal;

/// <summary>
/// 字段取值的两条判定：一个值能不能算「填了」（必填校验），勾选框的值长什么样（入库前规范化）。
/// </summary>
/// <remarks>
/// <para>
/// 勾选框的取值契约是 <c>true</c> / <c>false</c>（也认 <c>1</c> / <c>0</c>，不分大小写、忽略首尾空白），
/// 入库前一律规范成 <see cref="Checked"/> / <see cref="Unchecked"/>；必填的勾选框必须是勾上的。
/// </para>
/// <para>
/// ★ 提交校验与密封渲染必须用<b>同一个</b>判定。此前必填只看「非空白」，而密封只把 <c>true</c> / <c>1</c> 画成勾：
/// <c>false</c>、<c>0</c>、<c>no</c> 都过得了必填，成品上那个必勾的框是空的，完成证书照样写 Signed；
/// 反方向，HTML 复选框的默认值 <c>on</c> 也过得了，却被画成没勾。认不出的值现在当场拒绝，绝不静默画成空框。
/// </para>
/// </remarks>
internal static class SigningFieldValues
{
    /// <summary>勾上。</summary>
    public const string Checked = "true";

    /// <summary>没勾。</summary>
    public const string Unchecked = "false";

    /// <summary>勾选框的值是否表示「勾上」（密封据此画勾）。</summary>
    public static bool IsChecked(string? value)
    {
        var token = value?.Trim();
        return string.Equals(token, Checked, StringComparison.OrdinalIgnoreCase) || token == "1";
    }

    /// <summary>
    /// 规范化一个字段值：勾选框认 <c>true</c> / <c>false</c> / <c>1</c> / <c>0</c>，规范成 <see cref="Checked"/> / <see cref="Unchecked"/>；
    /// 其余类型原样返回。空白原样返回（「没填」由必填校验处理）。认不出的勾选框值返回 <see langword="false"/>。
    /// </summary>
    public static bool TryNormalize(SnapshotField field, string? value, out string? normalized)
    {
        Check.NotNull(field);

        normalized = value;
        if (field.Type != SigningFieldType.Checkbox || string.IsNullOrWhiteSpace(value))
            return true;

        if (IsChecked(value))
        {
            normalized = Checked;
            return true;
        }

        var token = value.Trim();
        if (string.Equals(token, Unchecked, StringComparison.OrdinalIgnoreCase) || token == "0")
        {
            normalized = Unchecked;
            return true;
        }

        return false;
    }

    /// <summary>
    /// 一个值能不能满足必填：勾选框必须是勾上的，其余类型非空白即可。
    /// </summary>
    public static bool IsFilled(SnapshotField field, string? value)
    {
        Check.NotNull(field);
        return field.Type == SigningFieldType.Checkbox ? IsChecked(value) : !string.IsNullOrWhiteSpace(value);
    }

    /// <summary>勾选框值不合契约时给人看的那句话（按字段标签指名）。</summary>
    public static string InvalidCheckboxMessage(SnapshotField field)
        => $"The value of '{field.Label}' must be 'true' or 'false'.";
}
