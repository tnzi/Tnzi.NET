using System.Text;

namespace Tnzi.Settings;

/// <summary>
/// 配置组 → 权限码的命名单一事实源（纯函数）。配置权限码统一格式：
/// <c>{group}.settings.{slug}.view</c> / <c>{group}.settings.{slug}.update</c>，
/// 其中 <c>group</c> 是归属权限组（挂在该模块已有的权限组下），<c>slug</c> 是配置组
/// 的实体面段。固定的 <c>.settings.</c> 中缀让每个配置组成为可独立分配的一对权限码，
/// 同时给前端一个可靠的模式识别"是否持有任一配置权限"。
/// </summary>
/// <remarks>
/// 桥接 provider（<c>SettingsPermissionDefinitionProvider</c>）用它派生并注入权限目录，
/// <c>SettingsCenterService</c> 用它做按组授权过滤 —— 两侧共用此函数保证码一致。
/// </remarks>
[ExperimentalApi(Reason = "Settings center permission model is new and may evolve")]
public static class SettingsPermissionNaming
{
    /// <summary>view/update 之间的公共段，前端据此识别配置权限码。</summary>
    public const string Infix = "settings";

    /// <summary>归属权限组 name：显式 <c>PermissionGroup</c> 优先，否则 ModuleName 规范化。</summary>
    public static string GroupName(SettingDefinitionGroup group)
    {
        Check.NotNull(group);
        if (!string.IsNullOrWhiteSpace(group.PermissionGroup))
            return group.PermissionGroup.Trim();
        return Normalize(group.ModuleName);
    }

    /// <summary>实体面段 slug：显式 <c>PermissionSlug</c> 优先，否则从 Key 去归属组前缀后 camelCase。</summary>
    public static string Slug(SettingDefinitionGroup group)
    {
        Check.NotNull(group);
        if (!string.IsNullOrWhiteSpace(group.PermissionSlug))
            return group.PermissionSlug.Trim();
        return DeriveSlug(group.Key, GroupName(group));
    }

    /// <summary><c>{group}.settings.{slug}.view</c></summary>
    public static string ViewCode(SettingDefinitionGroup group)
        => $"{GroupName(group)}.{Infix}.{Slug(group)}.view";

    /// <summary><c>{group}.settings.{slug}.update</c></summary>
    public static string UpdateCode(SettingDefinitionGroup group)
        => $"{GroupName(group)}.{Infix}.{Slug(group)}.update";

    /// <summary>是否为配置权限码（形如 <c>x.settings.y.view|update</c>）。</summary>
    public static bool IsSettingsPermissionCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;
        var parts = code.Split('.');
        return parts.Length == 4
            && parts[1].Equals(Infix, StringComparison.OrdinalIgnoreCase)
            && (parts[3].Equals("view", StringComparison.OrdinalIgnoreCase)
                || parts[3].Equals("update", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>小写化并仅保留字母数字（"AI" → "ai"，"Web" → "web"，"System" → "system"）。</summary>
    private static string Normalize(string? moduleName)
    {
        if (string.IsNullOrWhiteSpace(moduleName)) return "app";
        var sb = new StringBuilder(moduleName.Length);
        foreach (var ch in moduleName)
        {
            if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
        }
        return sb.Length > 0 ? sb.ToString() : "app";
    }

    /// <summary>
    /// 从组 Key 派生 slug：按 '-' 分段，若首段（不区分大小写）等于归属组则丢弃，
    /// 其余 camelCase 拼接。若丢弃后为空则退回整个 Key 的 camelCase。
    /// 例："chat-general"/chat → "general"；"web-observability"/system → "webObservability"。
    /// </summary>
    private static string DeriveSlug(string key, string groupName)
    {
        var segments = key.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0) return CamelCase([key]);
        if (segments.Length > 1 && segments[0].Equals(groupName, StringComparison.OrdinalIgnoreCase))
            return CamelCase(segments[1..]);
        return CamelCase(segments);
    }

    /// <summary>kebab 段数组 → camelCase：首段全小写，后续段首字母大写。</summary>
    private static string CamelCase(string[] segments)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < segments.Length; i++)
        {
            var seg = segments[i];
            if (seg.Length == 0) continue;
            if (sb.Length == 0)
            {
                sb.Append(seg.ToLowerInvariant());
            }
            else
            {
                sb.Append(char.ToUpperInvariant(seg[0]));
                if (seg.Length > 1) sb.Append(seg[1..].ToLowerInvariant());
            }
        }
        return sb.Length > 0 ? sb.ToString() : "general";
    }
}
