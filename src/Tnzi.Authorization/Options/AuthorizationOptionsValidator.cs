using Tnzi.Options;

namespace Tnzi.Authorization.Options;

/// <summary>
/// Authorization 模块配置选项验证器。
/// </summary>
/// <remarks>
/// 主要目标是**让常见的部署事故在启动时就显形**，而不是在生产里默默生效：
/// <list type="bullet">
///   <item>typo'd JSON key（<c>SuperAdminRole</c> vs <c>SuperAdminRoles</c>）→ List 仍是空，admin 在生产里突然全员失去 super 权限。无法纯靠 validator 检测（key 在 IConfiguration 阶段已被丢弃），但我们至少能在 List 为空时打 Info 日志提醒。</item>
///   <item>空字符串 / 重复值塞进数组 → 默默生效但语义不清，直接拒。</item>
/// </list>
/// 注意 validator 仅校验 options 自身的形态；"列出的 role 在 DB 是否存在" 这种
/// 跨模块校验放在 <c>AuthorizationModule.OnApplicationInitializationAsync</c>
/// 里做，因为校验时机要求 Identity 模块已经初始化。
/// </remarks>
public class AuthorizationOptionsValidator : OptionsValidatorBase<AuthorizationOptions>
{
    /// <inheritdoc />
    protected override void ValidateOptions(AuthorizationOptions options, List<string> errors)
    {
        // 超管角色列表形态校验。空 List 是合法（= 不启用超管短路），但若
        // 用户*提供*了 List 又写了垃圾值则提前 fail。
        ValidateRoleList(options.SuperAdminRoles, "Authorization.SuperAdminRoles", errors);

        // PermissionCategoryOverrides 键形态校验（值是枚举，绑定阶段已保证合法）。
        if (options.PermissionCategoryOverrides is { Count: > 0 })
        {
            foreach (var key in options.PermissionCategoryOverrides.Keys)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    errors.Add("Authorization.PermissionCategoryOverrides contains an empty or whitespace permission code key.");
                }
            }
        }
    }

    private static void ValidateRoleList(List<string>? roles, string optionPath, List<string> errors)
    {
        if (roles == null || roles.Count == 0) return;

        for (var i = 0; i < roles.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(roles[i]))
            {
                errors.Add($"{optionPath}[{i}] is empty or whitespace.");
            }
        }

        var duplicates = roles
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        if (duplicates.Count > 0)
        {
            errors.Add(
                $"{optionPath} contains duplicate entries (case-insensitive): " +
                $"{string.Join(", ", duplicates)}.");
        }
    }
}
