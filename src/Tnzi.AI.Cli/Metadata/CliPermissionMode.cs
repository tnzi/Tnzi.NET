namespace Tnzi.AI.Cli.Metadata;

/// <summary>
/// 一次运行里 agent 能不经批准做什么。
/// </summary>
/// <remarks>
/// <para>
/// 按 claude 2.1 的实测（同一句提示：用 Write 建文件、用 Bash <c>touch</c>、用 Bash 跑 <c>python3 -c</c> 写文件）：
/// </para>
/// <list type="table">
/// <listheader><term>档位</term><description>stream-json 的 <c>--permission-mode</c> / 效果</description></listheader>
/// <item><term><see cref="Full"/></term><description><c>bypassPermissions</c>：三个都放行（这个模式拒绝以 root 运行）</description></item>
/// <item><term><see cref="EditWorkspace"/></term><description><c>acceptEdits</c>：Write 与 <c>touch</c> 这类文件命令放行，其它命令拒绝</description></item>
/// <item><term><see cref="ReadOnly"/></term><description><c>default</c>：读文件、查资料照常，要权限的全拒（记在 <c>permission_denials</c>）</description></item>
/// <item><term><see cref="Ask"/></term><description><c>default</c>，要权限的操作交给批准通道（<c>can_use_tool</c> 等使用方回答）</description></item>
/// </list>
/// <para>
/// ACP 没有统一的模式开关，档位落在对 <c>session/request_permission</c> 的应答上：<see cref="Full"/> 放行，
/// <see cref="EditWorkspace"/> 只放行读、搜索与文件改动类的工具（<c>kind</c> 为 read / search / think / edit / delete / move），
/// <see cref="ReadOnly"/> 与 <see cref="Ask"/> 只放行读、搜索类；<see cref="Ask"/> 在有人值守时把其余的交给使用方批准。
/// </para>
/// <para>
/// ★ 刻意不用 <c>plan</c> 当只读：实测它只出计划、什么都不做，连读文件查资料回答问题这类不改东西的事也不做。
/// </para>
/// </remarks>
public enum CliPermissionMode
{
    /// <summary>全权：不经批准做任何事（缺省，与此前的行为相同）。</summary>
    Full = 0,

    /// <summary>改工作区：编辑文件放行，其余要权限的操作拒绝。</summary>
    EditWorkspace = 1,

    /// <summary>只读：不改任何东西，要权限的操作一律拒绝。</summary>
    ReadOnly = 2,

    /// <summary>
    /// 先问我：要权限的操作交给用户批准。
    /// </summary>
    /// <remarks>
    /// 这一轮有人值守时（<c>CliRunRequestDto.Attended</c> / <c>CliLaunchProfile.Attended</c>），CLI 的权限请求变成
    /// <c>PermissionRequest</c> 事件，等使用方回答（超时按拒绝，<c>AI:Cli:Approval:Timeout</c>）。没有人值守时没有人能批准，
    /// 效果与 <see cref="ReadOnly"/> 相同。
    /// </remarks>
    Ask = 3
}
