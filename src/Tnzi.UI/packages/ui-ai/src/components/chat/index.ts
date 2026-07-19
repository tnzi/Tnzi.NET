/**
 * Chat components
 *
 * Complete chat interface: messages, input, suggestions,
 * feedback, branching, attachments, and the main TChatBox container.
 *
 * The Manus-inspired shell primitives (reasoning stages, status banners,
 * task-done rows, follow-up lists, upgrade banners) are visual-only and
 * slot into any chat consumer that wants a Manus-style post-task UX.
 */

export { default as TScrollButton } from './TScrollButton.vue';
export { default as TConversationEmpty } from './TConversationEmpty.vue';
export { default as TMessageFeedback } from './TMessageFeedback.vue';
export type { FeedbackValue } from './TMessageFeedback.vue';
export { default as TMessageBranch } from './TMessageBranch.vue';
export { default as TMessageResponse } from './TMessageResponse.vue';
export { default as TMessageAttachments } from './TMessageAttachments.vue';
export { default as TChatMessage } from './TChatMessage.vue';
export { default as TMessageList } from './TMessageList.vue';
export { default as TSuggestions } from './TSuggestions.vue';
export type { SuggestionItem } from './TSuggestions.vue';
export { default as TPromptInput } from './TPromptInput.vue';
export { default as TChatBox } from './TChatBox.vue';

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
export type { ComposerAction } from './composer-types';
export { DEFAULT_COMPOSER_ACCEPT } from './composer-types';
export { default as TPopoverMenu } from './TPopoverMenu.vue';
export { default as TMessageActions } from './TMessageActions.vue';
export type { MessageAction } from './TMessageActions.vue';
export { default as TCitation } from './TCitation.vue';
export { default as TPromoCard } from './TPromoCard.vue';
export { default as TWorkspaceTopbar } from './TWorkspaceTopbar.vue';
