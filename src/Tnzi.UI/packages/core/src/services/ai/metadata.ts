/**
 * AI Module Metadata
 */

/**
 * Thread status
 */
export enum ThreadStatus {
    Active = 0,
    Completed = 1,
    Archived = 2,
}

/**
 * Get thread status label
 */
export function getThreadStatusLabel(status: ThreadStatus): string {
    switch (status) {
        case ThreadStatus.Active:
            return "Active";
        case ThreadStatus.Completed:
            return "Completed";
        case ThreadStatus.Archived:
            return "Archived";
        default:
            return "Unknown";
    }
}

/**
 * Message role
 */
export enum MessageRole {
    User = 0,
    Assistant = 1,
    System = 2,
    Tool = 3,
}

/**
 * Get message role label
 */
export function getMessageRoleLabel(role: MessageRole): string {
    switch (role) {
        case MessageRole.User:
            return "User";
        case MessageRole.Assistant:
            return "Assistant";
        case MessageRole.System:
            return "System";
        case MessageRole.Tool:
            return "Tool";
        default:
            return "Unknown";
    }
}

/**
 * Tool call status
 */
export enum ToolCallStatus {
    Pending = 0,
    Running = 1,
    Completed = 2,
    Failed = 3,
}

/**
 * Get tool call status label
 */
export function getToolCallStatusLabel(status: ToolCallStatus): string {
    switch (status) {
        case ToolCallStatus.Pending:
            return "Pending";
        case ToolCallStatus.Running:
            return "Running";
        case ToolCallStatus.Completed:
            return "Completed";
        case ToolCallStatus.Failed:
            return "Failed";
        default:
            return "Unknown";
    }
}

/**
 * Provider feature
 */
export enum ProviderFeature {
    Chat = "chat",
    Vision = "vision",
    Tools = "tools",
    Streaming = "streaming",
    Embeddings = "embeddings",
    FineTuning = "fineTuning",
}
