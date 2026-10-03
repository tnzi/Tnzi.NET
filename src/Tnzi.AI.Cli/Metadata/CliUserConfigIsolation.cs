namespace Tnzi.AI.Cli.Metadata;

/// <summary>
/// 外部 CLI 与宿主账号上那份「个人配置」的隔离程度。
/// </summary>
/// <remarks>
/// <para>
/// 编码 CLI 默认会读启动它的那个系统账号的全局配置（claude 是 <c>~/.claude</c>）：hooks、插件、
/// 个人技能、权限规则、全局记忆文件。开发机上这就是开发者本人的配置 —— 他的 hook 会在每次受管
/// 运行里触发，他的个人技能会出现在 agent 的可选列表里，行为随「由谁的账号起的进程」而变。
/// </para>
/// <para>
/// 三档之间的取舍是「隔离得越彻底，登录态越要另外准备」：个人配置与登录态住在同一个目录里。
/// </para>
/// </remarks>
public enum CliUserConfigIsolation
{
    /// <summary>不隔离：CLI 读宿主账号的全部个人配置。</summary>
    Inherit = 0,

    /// <summary>
    /// 排除个人设置文件：hooks、插件、个人技能、权限规则、环境变量等一律不加载，
    /// 但仍用宿主账号的登录态与会话存档。
    /// </summary>
    /// <remarks>
    /// ★ 实测（claude 2.1）<b>挡不住个人全局记忆文件</b>（<c>~/.claude/CLAUDE.md</c>）：它不属于「设置来源」。
    /// 要连它一起挡，用 <see cref="IsolatedConfigDirectory"/>。
    /// </remarks>
    ExcludeUserSettings = 1,

    /// <summary>
    /// 整个配置目录换成一个专用目录：个人配置一概看不见，登录态与会话存档也落在那个目录里。
    /// </summary>
    /// <remarks>
    /// 专用目录里起初没有登录态，需要在其中登录一次，或配置 <c>FallbackAuthToken</c> 兜底。
    /// </remarks>
    IsolatedConfigDirectory = 2
}
