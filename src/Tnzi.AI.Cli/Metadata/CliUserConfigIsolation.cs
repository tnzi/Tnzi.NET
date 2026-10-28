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
    IsolatedConfigDirectory = 2,

    /// <summary>
    /// 每个工作区一个配置目录（<c>{运行根}/.cli-config/{provider}</c>）：CLI 写下的一切，包括每个会话的完整记录，
    /// 都跟着工作区走，工作区被删（删除线程、回收器）时一起删掉。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 每线程工作区下就是每线程一个目录，同一线程的下一轮在同一个目录里续接会话；每运行工作区下每次运行一个。
    /// 用户提供的工作目录不支持（它的运行根按运行分，每一轮都续接不上前一轮），那样的运行以启动失败结束。
    /// </para>
    /// <para>
    /// 目录里<b>永远没有存储的登录</b>，也没有个人设置、全局记忆文件与插件：认证只能来自环境变量里的令牌
    /// （<c>FallbackAuthToken</c>，或经 <c>EnvironmentWhitelist</c> 透传的令牌 / API key）。
    /// 会话记录随工作区的回收而消失，所以线程闲置超过 <c>Gc:CompletedTtl</c> 之后的下一轮从新会话开始。
    /// </para>
    /// </remarks>
    PerWorkspaceConfigDirectory = 3,

    /// <summary>
    /// 配置目录与项目目录名都由执行位置（或调用方）按运行给出（<see cref="CliLaunchProfile.ConfigDirectory"/>、
    /// <see cref="CliLaunchProfile.ProjectDirectoryName"/>），支持用户提供的工作目录。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 典型用法是每个用户一个执行位置（容器）、每个用户一份配置目录（容器里的 <c>~/.claude</c>）：登录态、用户自己的设置与 MCP 都在里面，
    /// 会话记录按项目固定在 <c>&lt;配置目录&gt;/projects/&lt;项目目录名&gt;/</c>。同一个项目的不同 worktree（每个会话一个）续接的是同一份记录。
    /// </para>
    /// <para>
    /// 删除线程时按每一轮的会话 id 删掉对应的记录（provider 的 <c>SessionStorePaths</c>）；执行位置当时连不上的，
    /// 记录留到 CLI 自己按保留期清理（<see cref="CliLaunchProfile.SessionRetentionDays"/>）。
    /// </para>
    /// <para>档案没给出配置目录时运行以 <c>LaunchFailed</c> 结束。</para>
    /// </remarks>
    ProvidedConfigDirectory = 4
}
