/**
 * `@tnzi/ui-ai/chat` - the drop-in chat product shell.
 *
 * A dedicated build entry rather than part of `./components`: `TChatApp` plus
 * the parts it composes is ~82 kB gzipped, while the full component barrel is
 * ~183 kB. A consumer building a chat product should not pay for the workflow,
 * knowledge and skill domains it never renders.
 *
 * This is a **file, not a directory**, on purpose. It used to be `src/chat/`
 * holding `index.ts` + `TChatApp.vue` and nothing else, so the package had two
 * folders named `chat` (this one and `components/chat/`, which holds the 20-odd
 * parts `TChatApp` is assembled from). The component now lives with its parts;
 * this file keeps the subpath, so consumers are unaffected.
 *
 * Types come from wherever they are declared rather than being funnelled
 * through `TChatApp.vue`: forwarding them made the same name reachable twice
 * once the components stopped being split across two directories.
 */
export { default as TChatApp } from './components/chat/TChatApp.vue';
export type { ThemePref, ChatAppView, BuiltinViewId, ChatAppAttachmentsResult } from './components/chat/TChatApp.vue';
export type { ThreadItem } from './components/chat/TThreadList.vue';
export type { ThreadIcon, ThreadIconResolver } from './components/chat/thread-icon';
export { default as TRecentsPage } from './components/chat/TRecentsPage.vue';
export { RECENTS_VIEW_ID } from './headless/recents';
export type { RecentThreadsController, RecentsPreferences } from './headless/recents';
export type { ChatScrollMode } from './headless/useAutoScroll';
export type { LandingChip } from './components/chat/TLandingPage.vue';
export type { NavItem, NavGroup, NavGroupAction, NavItemAction, NavBadgeTone } from './components/layout/TSidebarNav.vue';
/* The rail's building blocks, for a product's own entries in the `#rail` /
   `#rail-threads` slots: an icon button with its tooltip, a button opening a
   panel beside the rail, the conversation list that panel shows by default,
   and the tooltip itself for any other icon-only control. */
export { default as TRailButton } from './components/layout/TRailButton.vue';
export { default as TRailFlyout } from './components/layout/TRailFlyout.vue';
export { default as TRailThreadList } from './components/chat/TRailThreadList.vue';
export type { RailThreadItem } from './components/chat/TRailThreadList.vue';
export { vTooltip, createTooltip } from './headless/tooltip';
export type { TooltipOptions, TooltipValue, TooltipController } from './headless/tooltip';
export type { FloatingSide } from './headless/place-beside';
export type { IconMotion } from './headless/icon-motion';
export type { UserMenuItem, UserBarAction } from './components/overlay/TUserMenu.vue';
export { useChatAside } from './headless/useAsidePanel';
export type { ChatAsideControl } from './headless/useAsidePanel';
export { useChatViewFill } from './headless/useChatViewFill';
export { default as TAsidePanel } from './components/layout/TAsidePanel.vue';
/* What the shell's own composer pieces take: the `/` menu's commands, and the cards a
   conversation waits on its user for (`approvals`, `describeApproval`). */
export type { SlashMenuItem } from './headless/slash-menu';
export type { SlashCommandsState } from './components/chat/TSlashCommandMenu.vue';
export type { ApprovalQueue, ApprovalSource, PendingInteraction } from './headless/approval-queue';
export type { ApprovalCardModel } from './headless/approval-card';
