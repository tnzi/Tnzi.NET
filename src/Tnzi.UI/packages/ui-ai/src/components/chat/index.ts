/**
 * Chat components
 *
 * Complete chat interface — messages, input, suggestions,
 * feedback, branching, attachments, and the main ChatBox container.
 *
 * The `T`-prefixed components below are Manus-inspired shell primitives
 * added in the shell rewrite: reasoning stages, status banners, task-done
 * rows, follow-up lists, upgrade banners. They are visual-only and slot
 * into any chat consumer that wants a Manus-style post-task UX.
 */

export { default as ScrollButton } from './ScrollButton.vue';
export { default as ConversationEmpty } from './ConversationEmpty.vue';
export { default as MessageActions } from './MessageActions.vue';
export { default as MessageFeedback } from './MessageFeedback.vue';
export type { FeedbackValue } from './MessageFeedback.vue';
export { default as MessageBranch } from './MessageBranch.vue';
export { default as MessageResponse } from './MessageResponse.vue';
export { default as MessageAttachments } from './MessageAttachments.vue';
export { default as ChatMessage } from './ChatMessage.vue';
export { default as MessageList } from './MessageList.vue';
export { default as Suggestions } from './Suggestions.vue';
export type { SuggestionItem } from './Suggestions.vue';
export { default as PromptInput } from './PromptInput.vue';
export { default as ChatBox } from './ChatBox.vue';

// Manus-inspired shell primitives (added after sidebar rewrite)
export { default as TReasoningStage } from './TReasoningStage.vue';
export type { ReasoningStageStatus } from './TReasoningStage.vue';
export { default as TStatusBanner } from './TStatusBanner.vue';
export type { StatusBannerVariant } from './TStatusBanner.vue';
export { default as TTaskDoneRow } from './TTaskDoneRow.vue';
export { default as TFollowUpList } from './TFollowUpList.vue';
export type { FollowUpItem } from './TFollowUpList.vue';
export { default as TUpgradeBanner } from './TUpgradeBanner.vue';
export { default as TThreadComposer } from './TThreadComposer.vue';
export { default as TPopoverMenu } from './TPopoverMenu.vue';
export { default as TMessageActions } from './TMessageActions.vue';
export type { MessageAction } from './TMessageActions.vue';
export { default as TCitation } from './TCitation.vue';
export { default as TPromoCard } from './TPromoCard.vue';
export { default as TWorkspaceTopbar } from './TWorkspaceTopbar.vue';
