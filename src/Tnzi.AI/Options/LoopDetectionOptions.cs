namespace Tnzi.AI.Options;

public class LoopDetectionOptions
{
    public bool Enabled { get; set; } = true;
    public int WarnThreshold { get; set; } = 3;
    public int HardLimit { get; set; } = 5;
    public int WindowSize { get; set; } = 20;
    public int MaxTrackedThreads { get; set; } = 100;
}
