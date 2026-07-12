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
/// <b>归属</b>：权限码挂到该配置组所属模块<b>已有</b>的权限组下（<c>parentName</c>）。因为
/// "配置组存在 ⟹ 该模块已加载 ⟹ 其权限 provider 已声明该组"，归属组总是存在（seeder 先收集
/// 全部 provider 再处理 permission，故 Define 顺序无关）；此 provider 因此<b>不</b> AddGroup，
/// 零覆盖既有组默认分类的风险。Web/AspNetCore 配置组经 <c>PermissionGroup="system"</c> 显式归到
/// system 组。分类统一 <see cref="PermissionCategory.Technical"/>：改部署级配置属运维敏感操作。
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
