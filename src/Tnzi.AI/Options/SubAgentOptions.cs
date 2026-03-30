namespace Tnzi.AI.Options;

public class SubAgentOptions
{
    public bool Enabled { get; set; } = true;
    public int MaxConcurrentSubAgents { get; set; } = 3;
    public int TimeoutSeconds { get; set; } = 900;
}
