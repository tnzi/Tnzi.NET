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
        // SuperAdmin 配置形态校验。空 List 是合法（= 不启用 super-admin
        // 旁路），但若用户*提供*了 List 又写了垃圾值则提前 fail。
        if (options.SuperAdminRoles != null && options.SuperAdminRoles.Count > 0)
        {
            for (var i = 0; i < options.SuperAdminRoles.Count; i++)
            {
                var name = options.SuperAdminRoles[i];
                if (string.IsNullOrWhiteSpace(name))
                {
                    errors.Add($"Authorization.SuperAdminRoles[{i}] is empty or whitespace.");
                }
            }

            var distinct = options.SuperAdminRoles
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n.Trim())
                .GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();
            if (distinct.Count > 0)
            {
                errors.Add(
                    $"Authorization.SuperAdminRoles contains duplicate entries (case-insensitive): " +
                    $"{string.Join(", ", distinct)}.");
            }
        }
    }
}
