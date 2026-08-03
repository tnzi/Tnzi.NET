export { useChat } from './useChat';
// Exported under its own name. The alias `ChatMessageData` that used to live here
// was justified by a collision with the `TChatMessage` component re-exported from
// ./components/chat/index.ts - but the component carries the `T` prefix, so the two
// names never actually collided. The alias only forced every consumer to rename it
// back on import.
export type { ChatMessage, UseChatReturn, UseChatOptions } from './useChat';
export { useStreamMarkdown } from './useStreamMarkdown';
export { useAutoScroll } from './useAutoScroll';
export { groupMessages } from './use-message-group';
export type { MessageGroupType, MessageGroup } from './use-message-group';
export { useMessageBranch } from './useMessageBranch';
export { useTokenCounter } from './useTokenCounter';
export type { ModelPricing, UseTokenCounterOptions, UseTokenCounterReturn } from './useTokenCounter';
export { useAgentExecution } from './useAgentExecution';
export type { AgentToolCall, HandoffEntry, AgentExecutionEvent, UseAgentExecutionReturn } from './useAgentExecution';
export { useWorkflowVisualization } from './useWorkflowVisualization';
export type { WorkflowNodeDef, WorkflowEdgeDef, WorkflowDefinition, UseWorkflowVisualizationReturn } from './useWorkflowVisualization';
export { useLocalSearch } from './useLocalSearch';
export type { UseLocalSearchReturn } from './useLocalSearch';
export { useSkillBrowser } from './useSkillBrowser';
export type { BrowsableSkill, SkillCategory, UseSkillBrowserReturn } from './useSkillBrowser';
export { useRagChat } from './useRagChat';
export type { RagCitation, UseRagChatReturn } from './useRagChat';
export { useEmbedMode } from './useEmbedMode';
export type { EmbedMode, UseEmbedModeReturn } from './useEmbedMode';
export { useSidebarState } from './useSidebarState';
export type { SidebarMode, UseSidebarStateOptions, UseSidebarStateReturn } from './useSidebarState';
export { useCommandPalette } from './useCommandPalette';
export type { CommandAction, UseCommandPaletteOptions, UseCommandPaletteReturn } from './useCommandPalette';
export { useSettingsDialog } from './useSettingsDialog';
export type { SettingsSection, UseSettingsDialogOptions, UseSettingsDialogReturn } from './useSettingsDialog';
export { useCodeHighlight, detectLangFromFilename } from './useCodeHighlight';
export type { CodeLang, CodeTheme, UseCodeHighlightOptions, UseCodeHighlightReturn } from './useCodeHighlight';
export { useVoiceInput } from './useVoiceInput';
export type { UseVoiceInputOptions, UseVoiceInputReturn } from './useVoiceInput';
export { useComposerAttachments } from './useComposerAttachments';
export type {
  UseComposerAttachmentsOptions,
  UseComposerAttachmentsReturn,
  RejectedAttachment,
  AttachmentRejectionReason,
} from './useComposerAttachments';
export { useAutoGrowTextarea } from './useAutoGrowTextarea';
export { useBodyScrollLock } from './useBodyScrollLock';
export { useGlobalAiTheme, AI_THEME_SCOPE } from './useGlobalAiTheme';
export type { UseGlobalAiThemeOptions, UseGlobalAiThemeReturn } from './useGlobalAiTheme';
export { useChatThreads } from './useChatThreads';
export type { UseChatThreadsOptions, UseChatThreadsReturn } from './useChatThreads';
