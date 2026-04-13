/**
 * AI Embed
 *
 * Embeddable chat widgets: floating bubble, sidebar panel, inline container,
 * and imperative API for any framework.
 */

export { default as FloatingChat } from './FloatingChat.vue';
export { default as SidebarChat } from './SidebarChat.vue';
export { default as InlineChat } from './InlineChat.vue';
export { createTnziChat } from './createTnziChat';
export type { TnziChatOptions, TnziChatInstance } from './createTnziChat';
