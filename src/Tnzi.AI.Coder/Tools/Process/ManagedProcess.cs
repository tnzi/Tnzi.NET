namespace Tnzi.AI.Coder.ProcessManagement;

/// <summary>
/// 托管进程状态
/// </summary>
internal class ManagedProcess
{
    public Process Process { get; set; } = null!;
    public StringBuilder Stdout { get; } = new();
    public StringBuilder Stderr { get; } = new();
    public string Command { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public string Id { get; set; } = string.Empty;
}
