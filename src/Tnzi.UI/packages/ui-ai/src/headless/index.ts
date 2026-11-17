export { useChat } from './useChat';
// Exported under its own name. The alias `ChatMessageData` that used to live here
// was justified by a collision with the `TChatMessage` component re-exported from
// ./components/chat/index.ts - but the component carries the `T` prefix, so the two
// names never actually collided. The alias only forced every consumer to rename it
// back on import.
export type {
  ChatMessage,
  ChatCompaction,
  ChatBackgroundTask,
  UseChatReturn,
  UseChatOptions,
  ToolCallInfo,
  TokenUsage,
  MessageAttachment,
  MessageRole,
} from './useChat';
export { useStreamMarkdown } from './useStreamMarkdown';
export type { StreamMarkdownLabels, UseStreamMarkdownOptions, UseStreamMarkdownReturn } from './useStreamMarkdown';
export { useAutoScroll, followTop } from './useAutoScroll';
export type { ChatScrollMode, UseAutoScrollOptions, UseAutoScrollReturn, FollowTurnOptions, FollowMetrics } from './useAutoScroll';
export { useTurnAnchor, reserveForAnchor } from './useTurnAnchor';
export type { UseTurnAnchorOptions, UseTurnAnchorReturn, TurnAnchorMetrics } from './useTurnAnchor';
export { useReasoningActivity } from './useReasoningActivity';
export type { ReasoningActivitySource, UseReasoningActivityReturn } from './useReasoningActivity';
export { useLatestInView, hiddenDistance } from './useLatestInView';
export type { UseLatestInViewOptions, UseLatestInViewReturn } from './useLatestInView';
export { usePinnedViewport, VIEWPORT_HEIGHT_VAR, VIEWPORT_TOP_VAR } from './usePinnedViewport';
export { useVisualViewportInsets, visualViewportInsets, VIEWPORT_INSET_TOP_VAR, VIEWPORT_INSET_BOTTOM_VAR } from './useVisualViewportInsets';
export type { ViewportInsets, UseVisualViewportInsetsOptions } from './useVisualViewportInsets';
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
export {
  useSidebarAutoFit,
  conversationRoom,
  decideSidebarFit,
  SIDEBAR_AUTO_FIT_MIN_WIDTH,
  SIDEBAR_AUTO_FIT_MARGIN,
  ASIDE_MAX_SHARE,
} from './useSidebarAutoFit';
export type {
  ConversationRoomInput,
  SidebarFitAction,
  SidebarFitState,
  UseSidebarAutoFitOptions,
  UseSidebarAutoFitReturn,
} from './useSidebarAutoFit';
export { useCommandPalette } from './useCommandPalette';
export type { CommandAction, UseCommandPaletteOptions, UseCommandPaletteReturn } from './useCommandPalette';
export { useSettingsDialog } from './useSettingsDialog';
export type { SettingsSection, SettingsSectionGroup, UseSettingsDialogOptions, UseSettingsDialogReturn } from './useSettingsDialog';
export { useViewportBelow } from './useViewportBelow';
export { createBackLayer, useBackLayer, installBackLayers, whenBackLayersSettled, BACK_LAYER_STATE_KEY } from './back-layers';
export type { BackLayer, BackLayerHandler, UseBackLayerOptions } from './back-layers';
export { enableTouchPress } from './touch-press';
export { preventPageZoom, withViewportValues } from './preventPageZoom';
export {
  createTitleReveal,
  vTitleReveal,
  TITLE_REVEAL_DELAY_MS,
  TITLE_REVEAL_SPEED_PX_PER_MS,
  TITLE_REVEAL_MIN_RANGE_PX,
  TITLE_REVEAL_HOST_ATTR,
} from './title-reveal';
export type { TitleRevealOptions, TitleRevealController } from './title-reveal';
export {
  createIconMotion,
  vIconMotion,
  isIconMotion,
  ICON_MOTIONS,
  ICON_MOTION_MAX_MS,
  ICON_MOTION_HOST_ATTR,
} from './icon-motion';
export type { IconMotion, IconMotionOptions, IconMotionController } from './icon-motion';
export {
  createTooltip,
  vTooltip,
  normalizeTooltip,
  TOOLTIP_DELAY_MS,
  TOOLTIP_SKIP_DELAY_MS,
  TOOLTIP_CLASS,
  TOOLTIP_SIDE_ATTRIBUTE,
  TOOLTIP_LONG_PRESS_MS,
  TOOLTIP_TOUCH_SHOW_MS,
  tooltipSide,
  isTruncated,
} from './tooltip';
export type { TooltipOptions, TooltipValue, TooltipController } from './tooltip';
export { createFlyoutController, FLYOUT_OPEN_DELAY_MS, FLYOUT_CLOSE_DELAY_MS } from './flyout';
export type { FlyoutState, FlyoutOptions, FlyoutController } from './flyout';
export { markAiApp, AI_APP_ATTRIBUTE } from './app-root';
export { placeBeside } from './place-beside';
export { eventPath, closestOnPath, startedWithin, trackEventPaths } from './event-path';
export { useElementSize } from './useElementSize';
export type { ElementSize, UseElementSizeOptions } from './useElementSize';
export type { FloatingSide, FloatingAlign, FloatingRect, FloatingPosition, PlaceBesideOptions } from './place-beside';
export { useCodeHighlight, detectLangFromFilename } from './useCodeHighlight';
export type { CodeLang, CodeTheme, UseCodeHighlightOptions, UseCodeHighlightReturn } from './useCodeHighlight';
export { useVoiceInput } from './useVoiceInput';
export type { UseVoiceInputOptions, UseVoiceInputReturn, VoiceInputErrorCode } from './useVoiceInput';
export { useComposerAttachments, isAcceptedFile, clipboardFiles } from './useComposerAttachments';
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
export {
  collectTasks,
  findToolCall,
  findToolCallOwner,
  formatTaskDuration,
  isBackgroundCommand,
  isListedTask,
  isSubAgentCall,
  mergeTaskCalls,
  mergeTaskDetail,
  mergeToolCall,
  replyToolCalls,
  subAgentSteps,
  taskElapsedMs,
  taskStatus,
  taskTitle,
  taskToolCount,
  upsertToolCalls,
} from './cli-tasks';
export type { SubAgentStepView, TaskEntry, TaskStatus } from './cli-tasks';
export { useNow } from './useNow';
export { useChatThreads } from './useChatThreads';
export { useThreadAttention, ATTENTION_RUNNING_POLL_MS, ATTENTION_IDLE_POLL_MS } from './useThreadAttention';
export type { ThreadAttention, ThreadAttentionApi, UseThreadAttentionOptions } from './useThreadAttention';
export type { UseChatThreadsOptions, UseChatThreadsReturn, ChatTurnOptions } from './useChatThreads';
export { useThreadReader, THREAD_FOLLOW_POLL_MS } from './useThreadReader';
export type { UseThreadReaderOptions, UseThreadReaderReturn } from './useThreadReader';
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
export {
  useAccountSettings,
  validateAvatarFile,
  cropAvatarSquare,
  AVATAR_OUTPUT_SIZE,
} from './useAccountSettings';
export type {
  UseAccountSettingsOptions,
  UseAccountSettingsReturn,
  AccountDraft,
  AvatarFailure,
  AccountDeletionFailure,
} from './useAccountSettings';
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
  compareThreads,
  formatRecentActivity,
  formatRecentsMonth,
  groupThreadsByDate,
  matchesRecentsStatus,
  normalizeRecentsPreferences,
  readRecentsPreferences,
  recentsDateKey,
  recentsQuery,
  sortThreads,
  threadOrderTime,
  threadTier,
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
  ThreadTier,
} from './recents';
export { useAsidePanel, useChatAside, provideChatAside, CHAT_ASIDE_KEY } from './useAsidePanel';
export type { UseAsidePanelOptions, UseAsidePanelReturn, ChatAsideControl } from './useAsidePanel';
export { useChatViewFill, provideChatViewFill, CHAT_VIEW_FILL_KEY } from './useChatViewFill';
export type { ChatViewFillControl } from './useChatViewFill';
export {
  codeDigits,
  secondsLeft,
  formatApprovalCountdown,
  allowsKind,
  permissionChoices,
} from './approval-card';
export type {
  ApprovalTone,
  ApprovalChoice,
  ApprovalField,
  ApprovalCode,
  ApprovalCardModel,
  PermissionChoiceLabels,
} from './approval-card';
export {
  ANSWER_MAX_LENGTH,
  EMPTY_PICK,
  pickOption,
  pickOwn,
  answersOf,
  isAnswered,
  questionAnswers,
  planChoices,
  questionOptionIds,
} from './interaction-card';
export type { QuestionCardModel, PlanCardModel, QuestionPick, PlanChoiceLabels } from './interaction-card';
export { createApprovalQueue, APPROVAL_POLL_MS } from './approval-queue';
export type {
  PendingInteraction,
  ApprovalAnswer,
  ApprovalAnswerOutcome,
  ApprovalSource,
  QueuedInteraction,
  ApprovalQueueOptions,
  ApprovalQueue,
} from './approval-queue';
export {
  RUN_REQUEST_SOURCE,
  RUN_REQUEST_CLOSED_CODES,
  ApprovalAnswerError,
  runRequestKind,
  runRequestKey,
  runRequestItem,
  runRequestFromEvent,
  createRunRequestSource,
  permissionDetail,
  permissionCardModel,
  questionCardModel,
  planCardModel,
} from './run-requests';
export type { RunRequestKind, RunRequestItem, RunRequestApi, RunRequestCardLabels } from './run-requests';
export { CHAT_THREADS_KEY, provideChatThreads, pickChatApprovals, pickChatMessageQueue, pickChatStopTask } from './chat-approvals-key';
export type { ChatThreadsBinding, ChatThreadsRegistry, PickedApprovals } from './chat-approvals-key';
export { createMessageQueue, QUEUE_POLL_MS, QUEUE_AFTER_TURN_MS, wentOut, waitsToGo, queuedLine, queuedText, isChangeable } from './message-queue';
export type {
  MessageQueue,
  MessageQueueApi,
  MessageQueueOperation,
  MessageQueueOptions,
  QueuedLine,
} from './message-queue';
export { vScrollHint, hasMoreBelow } from './scroll-hint';
export { useSlashMenu, slashQuery, filterSlashItems, textWithCommand } from './slash-menu';
export type { SlashMenuItem, SlashMenuKey, UseSlashMenuOptions, UseSlashMenuReturn } from './slash-menu';
export {
  CONTEXT_WARN_SHARE,
  CONTEXT_FULL_SHARE,
  compactTokens,
  contextShare,
  contextLevel,
  sharePercent,
  contextUsageOf,
} from './context-usage';
export type { ContextLevel, ContextUsage } from './context-usage';
