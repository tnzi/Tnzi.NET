namespace Tnzi.AI.Options;

[ConfigSection("AI:LoopDetection")]
[RuntimeSettingGroup(Key = "ai-conversation", Module = "AI", DisplayName = "Conversation",
    I18nKey = "admin.modules.system.settings.groups.aiConversation", Icon = "mdi:message-cog-outline", Order = 160)]
public class LoopDetectionOptions
{
    [RuntimeSetting(Label = "Loop Detection Enabled", I18n = "admin.modules.system.settings.fields.loopDetectionEnabled",
        Type = SettingFieldType.Boolean, Subsection = "Loop Detection")]
    public bool Enabled { get; set; } = true;

    [RuntimeSetting(Label = "Loop Warn Threshold", I18n = "admin.modules.system.settings.fields.loopDetectionWarnThreshold",
        Type = SettingFieldType.Int, Min = 1, Max = 20, Subsection = "Loop Detection",
        Description = "Number of repeated tool call patterns before a warning is injected")]
    public int WarnThreshold { get; set; } = 3;

    [RuntimeSetting(Label = "Loop Hard Limit", I18n = "admin.modules.system.settings.fields.loopDetectionHardLimit",
        Type = SettingFieldType.Int, Min = 1, Max = 50, Subsection = "Loop Detection",
        Description = "Number of repeated tool call patterns that forces the agent to stop using tools")]
    public int HardLimit { get; set; } = 5;

    [RuntimeSetting(Label = "Loop Window Size", I18n = "admin.modules.system.settings.fields.loopDetectionWindowSize",
        Type = SettingFieldType.Int, Min = 1, Subsection = "Loop Detection",
        Description = "Sliding window of recent tool-call hashes tracked per thread")]
    public int WindowSize { get; set; } = 20;

    [RuntimeSetting(Label = "Max Tracked Threads", I18n = "admin.modules.system.settings.fields.loopDetectionMaxTrackedThreads",
        Type = SettingFieldType.Int, Min = 1, Subsection = "Loop Detection",
        Description = "Maximum number of threads kept in the loop-detection LRU cache")]
    public int MaxTrackedThreads { get; set; } = 100;
}
