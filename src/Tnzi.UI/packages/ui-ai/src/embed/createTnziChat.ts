/**
 * createTnziChat — Imperative API for embedding @tnzi/ui-ai in any framework
 *
 * Usage:
 *   const chat = createTnziChat({ mode: 'floating', el: '#chat', apiBaseUrl: '/api' })
 *   chat.open()
 *   chat.sendMessage('Hello')
 *   chat.destroy()
 */

import { createApp, ref, h, type App } from 'vue';
import FloatingChat from './FloatingChat.vue';
import SidebarChat from './SidebarChat.vue';
import InlineChat from './InlineChat.vue';
import type { ChatMessage } from '@/composables/useChat';

export interface TnziChatOptions {
  /** Embed mode. */
  mode: 'floating' | 'sidebar' | 'inline';
  /** Target DOM element or selector (for inline mode). */
  el?: string | HTMLElement;
  /** API base URL for chat endpoint. */
  apiBaseUrl?: string;
}

export interface TnziChatInstance {
  /** Open the chat widget. */
  open: () => void;
  /** Close the chat widget. */
  close: () => void;
  /** Send a message programmatically. */
  sendMessage: (text: string) => void;
  /** Destroy the chat widget and clean up. */
  destroy: () => void;
}

export function createTnziChat(options: TnziChatOptions): TnziChatInstance {
  const messages = ref<ChatMessage[]>([]);
  const inputText = ref('');
  const isStreaming = ref(false);

  const components = {
    floating: FloatingChat,
    sidebar: SidebarChat,
    inline: InlineChat,
  } as const;

  const component = components[options.mode];

  // Create mount point
  let mountEl: HTMLElement;
  if (options.mode === 'inline' && options.el) {
    mountEl = typeof options.el === 'string' ? document.querySelector(options.el)! : options.el;
  } else {
    mountEl = document.createElement('div');
    mountEl.id = 'tnzi-chat-root';
    document.body.appendChild(mountEl);
  }

  const isOpen = ref(false);

  let app: App | null = createApp({
    setup() {
      return () =>
        h(component, {
          messages: messages.value,
          isStreaming: isStreaming.value,
          inputText: inputText.value,
          open: isOpen.value,
          'onUpdate:open': (v: boolean) => {
            isOpen.value = v;
          },
          'onUpdate:inputText': (v: string) => {
            inputText.value = v;
          },
          onSend: (content: string) => {
            messages.value = [
              ...messages.value,
              {
                id: crypto.randomUUID(),
                role: 'user',
                content,
                createdAt: new Date().toISOString(),
              },
            ];
          },
        });
    },
  });

  app.mount(mountEl);

  return {
    open() {
      isOpen.value = true;
    },
    close() {
      isOpen.value = false;
    },
    sendMessage(text: string) {
      messages.value = [
        ...messages.value,
        {
          id: crypto.randomUUID(),
          role: 'user',
          content: text,
          createdAt: new Date().toISOString(),
        },
      ];
    },
    destroy() {
      app?.unmount();
      app = null;
      if (options.mode !== 'inline' && mountEl.parentNode) {
        mountEl.parentNode.removeChild(mountEl);
      }
    },
  };
}
