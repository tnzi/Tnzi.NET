/**
 * Chat components
 *
 * Complete chat interface: messages, input, suggestions,
 * feedback, branching, attachments, and the main TChatBox container.
 *
 * The Manus-inspired shell primitives (status banners, task-done rows,
 * follow-up lists, upgrade banners) are visual-only and slot into any chat
 * consumer that wants a Manus-style post-task UX.
 *
 * Three former residents moved out because they are not chat concerns:
 * `TPopoverMenu` -> `components/overlay/`, `TWorkspaceTopbar` ->
 * `components/layout/`, `TReasoningStage` -> `components/reasoning/` (next to
 * `TReasoning` / `TChainOfThought`, which it was separated from).
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
export { default as TStatusBanner } from './TStatusBanner.vue';
export type { StatusBannerVariant } from './TStatusBanner.vue';
export { default as TTaskDoneRow } from './TTaskDoneRow.vue';
export { default as TFollowUpList } from './TFollowUpList.vue';
export type { FollowUpItem } from './TFollowUpList.vue';
export { default as TUpgradeBanner } from './TUpgradeBanner.vue';
export { default as TThreadComposer } from './TThreadComposer.vue';
export { default as TQueuedMessages } from './TQueuedMessages.vue';
export { default as TSlashCommandMenu } from './TSlashCommandMenu.vue';
export type { SlashCommandsState } from './TSlashCommandMenu.vue';
export { default as TThreadMessage } from './TThreadMessage.vue';
export { default as TUsageLimitNotice } from './TUsageLimitNotice.vue';
export { default as TComposerModelPicker } from './TComposerModelPicker.vue';
export type { ComposerModelOption } from './TComposerModelPicker.vue';
export { default as TThreadLoading } from './TThreadLoading.vue';
export type { ComposerAction } from './composer-types';
export type { ComposerSuggestionContext, ComposerTextRange } from './composer-suggestions';
export { tokenBeforeCaret, replaceTextRange } from './composer-suggestions';
export { DEFAULT_COMPOSER_ACCEPT, DEFAULT_SEND_ICON } from './composer-types';
export { canQueueContent, composerPrimaryAction } from './composer-primary-action';
export type {
  ComposerPrimaryAction,
  ComposerPrimaryActionInput,
  ComposerPrimaryActionKind,
} from './composer-primary-action';
export { default as TMessageActions } from './TMessageActions.vue';
export type { MessageAction } from './TMessageActions.vue';
export { default as TCitation } from './TCitation.vue';
export { default as TPromoCard } from './TPromoCard.vue';

/* `TChatApp` is the drop-in chat product shell. It used to sit in a top-level
   `src/chat/` directory holding nothing else, which put it in a different place
   from the 20-odd parts it composes - and gave the package two `chat` folders.
   The `@tnzi/ui-ai/chat` subpath still exists (see src/chat-app.ts); only the
   file moved. */
export { default as TChatApp } from './TChatApp.vue';
export type { ThemePref, ChatAppView, BuiltinViewId, ChatAppAttachmentsResult } from './TChatApp.vue';
export { default as TThreadList } from './TThreadList.vue';
export type { ThreadItem } from './TThreadList.vue';
export { default as TRecentsPage } from './TRecentsPage.vue';
export { default as TRailThreadList } from './TRailThreadList.vue';
export type { RailThreadItem } from './TRailThreadList.vue';
export { default as TThreadIcon } from './TThreadIcon.vue';
export { default as TThreadStatus } from './TThreadStatus.vue';
export { resolveThreadIcon } from './thread-icon';
export type { ThreadIcon, ThreadIconResolver } from './thread-icon';
export { default as TThreadTitle } from './TThreadTitle.vue';
export { default as TLandingPage } from './TLandingPage.vue';
export type { LandingChip } from './TLandingPage.vue';
export { default as TFileCard } from './TFileCard.vue';
export { default as TAttachmentImage } from './TAttachmentImage.vue';
export { provideImageGroup, createImageGroup, useImageGroup } from './image-group';
export type { ImageGroup, ImageGroupMember, ViewerImage } from './image-group';
export {
  provideChatRenderContext,
  provideToolRenderers,
  useChatRenderContext,
  resolveToolRenderer,
  resolveToolRendererEntry,
  defaultToolFileHref,
  TOOL_RENDERERS_KEY,
} from './chat-render-context';
export type {
  ChatRenderContext,
  ChatFileRef,
  ToolRenderer,
  ToolRendererEntry,
  ToolRendererMap,
  ToolRendererProps,
} from './chat-render-context';
export { replyParts, segmentReply } from './reply-segments';
export type { ReplyPart, ReplySegment, SegmentedReply } from './reply-segments';
