namespace Tnzi.System.Settings;

/// <summary>
/// 把配置中心的每个 <see cref="SettingDefinitionGroup"/> 派生成一对可分配权限码
/// （<c>{group}.settings.{slug}.view</c> / <c>.update</c>），注入权限目录，让运营能在
/// 角色权限矩阵里按模块细粒度授予"查看/修改某模块配置"，而不再是"全有或全无"。
/// </summary>
/// <remarks>
/// <para>
/// 桥接了两个原本割裂的世界：配置组是<b>运行时</b>从 <c>[RuntimeSetting]</c> 扫描动态发现的，
/// 权限码是<b>启动期</b>由 <see cref="IPermissionDefinitionProvider"/> 声明的。此 provider 在
/// <c>PermissionDbSeeder</c> 收集阶段遍历所有配置组，用 <see cref="SettingsPermissionNaming"/>
/// 派生码 —— 与 <c>SettingsCenterService</c> 的按组授权过滤共用同一命名函数，保证两侧一致。
/// </para>
/// <para>
/// <b>归属</b>：权限码挂到该配置组所属模块<b>已有</b>的权限组下（<c>parentName</c>），此 provider
/// 自己<b>不</b> AddGroup —— 它在 <c>Define</c> 那一刻无从知道别的 provider 会不会声明这个组
/// （provider 之间无顺序保证），而无条件 AddGroup 会让"谁先跑"决定组的 DisplayName 与默认分类。
/// Web/AspNetCore 配置组经 <c>PermissionGroup="system"</c> 显式归到 system 组。分类统一
/// <see cref="PermissionCategory.Technical"/>：改部署级配置属运维敏感操作。
/// </para>
/// <para>
/// <b>★归属组不存在时会静默丢码，这是本机制唯一的失败面。</b>此处曾写着不变式「配置组存在 ⟹
/// 模块已加载 ⟹ 其权限组已声明」—— <b>它不成立</b>：模块可以有配置组却没有任何权限组
/// （<c>Tnzi.Identity.Presence</c> 一个 admin 控制器都没有），也可以把权限码声明在与模块名不同的
/// 组下（<c>Tnzi.AspNetCore</c> 的 <c>Module = "Web"</c> 归到 <c>system</c>）。归属组不存在时
/// <c>PermissionDbSeeder</c> 只记一行 warning 就跳过这两个码，于是那一组配置在角色权限矩阵里
/// 连行都没有、只有超管靠 bypass 能改，而没有任何东西会报错。
/// <c>[RuntimeSettingGroup(PermissionGroup = "...")]</c> 是显式指定归属组的口子；框架侧由
/// <c>tests/Tnzi.Architecture.Tests/SettingsPermissionGroupResolutionTests</c> 全模块图零 allowlist 把关。
/// </para>
/// </remarks>
public sealed class SettingsPermissionDefinitionProvider : IPermissionDefinitionProvider
{
    private readonly IEnumerable<ISettingDefinitionProvider> _settingProviders;

    public SettingsPermissionDefinitionProvider(IEnumerable<ISettingDefinitionProvider> settingProviders)
    {
        _settingProviders = Check.NotNull(settingProviders);
    }

    public void Define(IPermissionDefinitionContext context)
    {
        Check.NotNull(context);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in _settingProviders)
        {
            foreach (var group in provider.GetGroups())
            {
                var prefix = $"{SettingsPermissionNaming.GroupName(group)}.{SettingsPermissionNaming.Infix}.{SettingsPermissionNaming.Slug(group)}";
                if (!seen.Add(prefix))
                    continue;

                context.AddCrudPermissions(
                    prefix,
                    $"{group.DisplayName} Settings",
                    parentName: SettingsPermissionNaming.GroupName(group),
                    category: PermissionCategory.Technical,
                    actions: CrudActions.View | CrudActions.Update);
            }
        }
    }
}
