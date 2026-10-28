export { useChat } from './useChat';
// Exported under its own name. The alias `ChatMessageData` that used to live here
// was justified by a collision with the `TChatMessage` component re-exported from
// ./components/chat/index.ts - but the component carries the `T` prefix, so the two
// names never actually collided. The alias only forced every consumer to rename it
// back on import.
export type {
  ChatMessage,
  UseChatReturn,
  UseChatOptions,
  ToolCallInfo,
  TokenUsage,
  MessageAttachment,
  MessageRole,
} from './useChat';
export { useStreamMarkdown } from './useStreamMarkdown';
export type { StreamMarkdownLabels, UseStreamMarkdownOptions, UseStreamMarkdownReturn } from './useStreamMarkdown';
export { useAutoScroll } from './useAutoScroll';
export { useTurnAnchor, reserveForAnchor } from './useTurnAnchor';
export type { UseTurnAnchorOptions, UseTurnAnchorReturn, TurnAnchorMetrics } from './useTurnAnchor';
export { useLatestInView, hiddenDistance } from './useLatestInView';
export type { UseLatestInViewOptions, UseLatestInViewReturn } from './useLatestInView';
export { usePinnedViewport, VIEWPORT_HEIGHT_VAR, VIEWPORT_TOP_VAR } from './usePinnedViewport';
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
export type { BrowsableSkill, SkillBrowserApi, SkillCategory, UseSkillBrowserReturn } from './useSkillBrowser';
export { useRagChat } from './useRagChat';
export type { RagCitation, UseRagChatReturn } from './useRagChat';
export { useEmbedMode } from './useEmbedMode';
export type { EmbedMode, UseEmbedModeReturn } from './useEmbedMode';
export { useSidebarState } from './useSidebarState';
export type { SidebarMode, UseSidebarStateOptions, UseSidebarStateReturn } from './useSidebarState';
export { useCommandPalette } from './useCommandPalette';
export type { CommandAction, UseCommandPaletteOptions, UseCommandPaletteReturn } from './useCommandPalette';
export { useSettingsDialog } from './useSettingsDialog';
export type { SettingsSection, SettingsNarrowView, UseSettingsDialogOptions, UseSettingsDialogReturn } from './useSettingsDialog';
export { useViewportBelow } from './useViewportBelow';
export { preventPageZoom, withViewportValues } from './preventPageZoom';
export { useCodeHighlight, detectLangFromFilename } from './useCodeHighlight';
export type { CodeLang, CodeTheme, UseCodeHighlightOptions, UseCodeHighlightReturn } from './useCodeHighlight';
export { useVoiceInput } from './useVoiceInput';
export type { UseVoiceInputOptions, UseVoiceInputReturn, VoiceInputErrorCode } from './useVoiceInput';
export { useComposerAttachments, isAcceptedFile } from './useComposerAttachments';
export type {
  UseComposerAttachmentsOptions,
  UseComposerAttachmentsReturn,
  RejectedAttachment,
  AttachmentRejectionReason,
  AddAttachmentsResult,
} from './useComposerAttachments';
export { useAutoGrowTextarea } from './useAutoGrowTextarea';
export { useBodyScrollLock } from './useBodyScrollLock';
export { useGlobalAiTheme, AI_THEME_SCOPE } from './useGlobalAiTheme';
export type { UseGlobalAiThemeOptions, UseGlobalAiThemeReturn } from './useGlobalAiTheme';
export { useAuthPage } from './useAuthPage';
export type { UseAuthPageOptions, UseAuthPageReturn, AuthStep } from './useAuthPage';
export { useChatThreads } from './useChatThreads';
export type { UseChatThreadsOptions, UseChatThreadsReturn, ChatTurnOptions } from './useChatThreads';
export { toChatThreadsError } from './chat-errors';
export type { ChatThreadsError, ChatThreadsOperation } from './chat-errors';
export { stashWelcomeMessage, takeWelcomeMessage } from './welcome-message';
export { useAttachmentUrl } from './useAttachmentUrl';
export type { UseAttachmentUrlReturn } from './useAttachmentUrl';
export { filesToContentParts, AttachmentRefusedError } from './attachment-parts';
export type {
  FilesToContentPartsOptions,
  ContentPartsResult,
  ChatContentPart,
  UploadedAttachment,
  AttachmentRefusalReason,
  AttachmentRefusedErrorOptions,
} from './attachment-parts';
export { useAiPersonalization } from './useAiPersonalization';
export type {
  UseAiPersonalizationOptions,
  UseAiPersonalizationReturn,
  AiPersonalizationDraft,
} from './useAiPersonalization';
export { useAccountSettings } from './useAccountSettings';
export type {
  UseAccountSettingsOptions,
  UseAccountSettingsReturn,
  AccountDraft,
} from './useAccountSettings';
export { useAiNaiveTheme } from './useAiNaiveTheme';
export type { UseAiNaiveThemeReturn } from './useAiNaiveTheme';
export { useAiUsage, usageBarPercent } from './useAiUsage';
export type { UseAiUsageOptions, UseAiUsageReturn, UsageLimitWarning } from './useAiUsage';
export {
  USAGE_LIMIT_REACHED_ERROR_CODE,
  CLI_USAGE_LIMIT_ERROR_CODE,
  RELATIVE_RESET_HORIZON_MS,
  readUsageLimitDenial,
  formatCountdown,
  formatResetDay,
  describeReset,
  formatResetLabel,
  formatResetSentence,
  formatLimitReached,
  formatWindowDuration,
  wholePercent,
  isResetAhead,
} from './usage-limits';
export type { UsageLimitNotice } from './usage-limits';
export { useChatModels } from './useChatModels';
export type { UseChatModelsOptions, UseChatModelsReturn } from './useChatModels';
export {
  DEFAULT_RECENTS_PREFERENCES,
  RECENTS_ACTIVITY_WINDOWS,
  RECENTS_GROUP_BYS,
  RECENTS_SORTS,
  RECENTS_STATUSES,
  RECENTS_STORAGE_KEY,
  RECENTS_VIEW_ID,
  changesRecentsQuery,
  formatRecentActivity,
  formatRecentsMonth,
  groupThreadsByDate,
  matchesRecentsStatus,
  normalizeRecentsPreferences,
  readRecentsPreferences,
  recentsDateKey,
  recentsQuery,
  writeRecentsPreferences,
} from './recents';
export type {
  RecentThreadsController,
  RecentThreadsPage,
  RecentsActivityWindow,
  RecentsDateGroup,
  RecentsGroupBy,
  RecentsPreferences,
  RecentsSort,
  RecentsStatus,
} from './recents';
export { useAsidePanel, useChatAside, provideChatAside, CHAT_ASIDE_KEY } from './useAsidePanel';
export type { UseAsidePanelOptions, UseAsidePanelReturn, ChatAsideControl } from './useAsidePanel';
