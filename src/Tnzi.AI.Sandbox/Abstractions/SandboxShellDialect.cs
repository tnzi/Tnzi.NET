namespace Tnzi.AI.Sandbox.Abstractions;

/// <summary>
/// 沙箱执行 <see cref="ISandbox.ExecuteCommandAsync"/> 所用 shell 的引号语法。
/// </summary>
/// <remarks>
/// bash 工具把命令里的 <c>/mnt/*</c> 换成真实路径后才交给 shell，而真实路径可以含空格
/// （macOS 的 <c>~/Library/Application Support</c>、带空格的 Windows 用户名）。要把它作为一个词交出去，
/// 就得按执行它的那个 shell 的规则加引号。
/// </remarks>
public enum SandboxShellDialect
{
    /// <summary>POSIX shell（bash / sh）：单引号、双引号、反斜杠转义。</summary>
    Posix = 0,

    /// <summary>Windows <c>cmd.exe</c>：只有双引号，<c>^</c> 在引号外转义。</summary>
    Cmd = 1
}
