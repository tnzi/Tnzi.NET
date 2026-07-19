export { useChat } from './useChat';
// NOTE: ChatMessage type is aliased to ChatMessageData here to avoid collision with the
// TChatMessage Vue component re-exported from ./components/chat/index.ts at the top-level barrel.
// Consumers importing from '@tnzi/ui-ai/composables' can still access the raw type via useChat's return.
export type { ChatMessage as ChatMessageData, UseChatReturn, UseChatOptions } from './useChat';
export { useStreamMarkdown } from './useStreamMarkdown';
export { useAutoScroll } from './useAutoScroll';
export { groupMessages } from './useMessageGroup';
export type { MessageGroupType, MessageGroup } from './useMessageGroup';
export { useMessageBranch } from './useMessageBranch';
export { useTokenCounter } from './useTokenCounter';
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
export type { UseComposerAttachmentsOptions, UseComposerAttachmentsReturn } from './useComposerAttachments';
export { useAutoGrowTextarea } from './useAutoGrowTextarea';
