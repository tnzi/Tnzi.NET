/**
 * createTnziChat - imperative API for embedding @tnzi/ui-ai in any framework.
 *
 * Mounts one of the embed widgets into the page and hands back a small
 * imperative handle. Two transport modes:
 *
 * **Server-backed** - pass `apiBaseUrl` (plus `headers` for auth) and the
 * widget streams from the Tnzi.NET chat endpoint (`POST {apiBaseUrl}/chat/stream`)
 * through `streamChat()` from `@tnzi/core`:
 *
 * ```ts
 * const chat = createTnziChat({
 *   mode: 'floating',
 *   apiBaseUrl: '/api',
 *   headers: { Authorization: `Bearer ${token}` },
 * })
 * chat.open()
 * chat.sendMessage('Hello')
 * chat.destroy()
 * ```
 *
 * **Bring your own transport** - omit `apiBaseUrl` and drive the widget from
 * the outside. `onSend` fires for every user message, and `pushMessage` /
 * `appendDelta` / `setStreaming` feed the reply back in:
 *
 * ```ts
 * const chat = createTnziChat({
 *   mode: 'inline',
 *   el: '#chat',
 *   onSend: async (text, api) => {
 *     const id = api.pushMessage({ role: 'assistant', content: '' })
 *     api.setStreaming(true)
 *     for await (const chunk of myStream(text)) api.appendDelta(id, chunk)
 *     api.setStreaming(false)
 *   },
 * })
 * ```
 */

import { createApp, ref, h, type App } from 'vue';
import { streamChat } from '@tnzi/core/services/ai';
import TFloatingChat from './TFloatingChat.vue';
import TSidebarChat from './TSidebarChat.vue';
import TInlineChat from './TInlineChat.vue';
import type { ChatMessage, MessageRole } from '@/composables/useChat';

/** Handle passed to `onSend` for pushing a reply back into the widget. */
export interface TnziChatTransportApi {
  /** Append a message and return its generated id. */
  pushMessage: (message: Partial<ChatMessage> & { role: MessageRole }) => string;
  /** Append text to an existing message's `content`. */
  appendDelta: (id: string, delta: string) => void;
  /** Patch an existing message. */
  updateMessage: (id: string, patch: Partial<ChatMessage>) => void;
  /** Toggle the widget's streaming indicator. */
  setStreaming: (value: boolean) => void;
  /** Aborts when the widget is destroyed or `stop()` is called. */
  signal: AbortSignal;
}

export interface TnziChatOptions {
  /** Embed mode. */
  mode: 'floating' | 'sidebar' | 'inline';
  /** Target DOM element or selector. Required for inline mode. */
  el?: string | HTMLElement;
  /**
   * Base URL of the Tnzi.NET API. When set, the widget streams from
   * `{apiBaseUrl}/chat/stream` and `onSend` is not used.
   */
  apiBaseUrl?: string;
  /** Extra request headers (typically `Authorization`). Server-backed mode only. */
  headers?: Record<string, string>;
  /** Agent to route to. Server-backed mode only. */
  agentId?: string | null;
  /** Model override. Server-backed mode only. */
  model?: string | null;
  /** Thread to continue. Server-backed mode only; updated as the server assigns one. */
  threadId?: string | null;
  /** Called for every user message when `apiBaseUrl` is not set. */
  onSend?: (text: string, api: TnziChatTransportApi) => void | Promise<void>;
  /** Called when a request fails. */
  onError?: (error: Error) => void;
}

export interface TnziChatInstance {
  /** Open the chat widget. */
  open: () => void;
  /** Close the chat widget. */
  close: () => void;
  /** Send a message programmatically. */
  sendMessage: (text: string) => void;
  /** Abort the in-flight response, if any. */
  stop: () => void;
  /** Destroy the chat widget and clean up. */
  destroy: () => void;
}

let idCounter = 0;

/** `crypto.randomUUID` needs a secure context; embeds routinely run without one. */
function generateId(): string {
  const cryptoObj = typeof globalThis !== 'undefined' ? globalThis.crypto : undefined;
  if (cryptoObj && typeof cryptoObj.randomUUID === 'function') {
    return cryptoObj.randomUUID();
  }
  idCounter += 1;
  return `tnzi_${Date.now().toString(36)}_${idCounter}`;
}

function resolveMountEl(options: TnziChatOptions): { el: HTMLElement; owned: boolean } {
  if (options.mode === 'inline') {
    if (!options.el) {
      throw new Error('createTnziChat: `el` is required for inline mode.');
    }
    const found =
      typeof options.el === 'string' ? document.querySelector<HTMLElement>(options.el) : options.el;
    if (!found) {
      throw new Error(`createTnziChat: no element matched "${String(options.el)}".`);
    }
    return { el: found, owned: false };
  }

  const created = document.createElement('div');
  created.id = 'tnzi-chat-root';
  document.body.appendChild(created);
  return { el: created, owned: true };
}

export function createTnziChat(options: TnziChatOptions): TnziChatInstance {
  const messages = ref<ChatMessage[]>([]);
  const inputText = ref('');
  const isStreaming = ref(false);
  const isOpen = ref(false);

  const components = {
    floating: TFloatingChat,
    sidebar: TSidebarChat,
    inline: TInlineChat,
  } as const;

  const component = components[options.mode];
  const { el: mountEl, owned: ownsMountEl } = resolveMountEl(options);

  let destroyed = false;
  let abortController: AbortController | null = null;
  let threadId: string | null = options.threadId ?? null;

  function pushMessage(message: Partial<ChatMessage> & { role: MessageRole }): string {
    const id = message.id ?? generateId();
    messages.value = [
      ...messages.value,
      {
        content: '',
        createdAt: new Date().toISOString(),
        ...message,
        id,
      },
    ];
    return id;
  }

  function updateMessage(id: string, patch: Partial<ChatMessage>): void {
    messages.value = messages.value.map((m) => (m.id === id ? { ...m, ...patch } : m));
  }

  function appendDelta(id: string, delta: string): void {
    if (!delta) return;
    messages.value = messages.value.map((m) =>
      m.id === id ? { ...m, content: m.content + delta } : m,
    );
  }

  function setStreaming(value: boolean): void {
    isStreaming.value = value;
  }

  function reportError(error: Error, assistantId: string | null): void {
    if (assistantId) {
      updateMessage(assistantId, { isStreaming: false, status: 'error', error: error.message });
    }
    setStreaming(false);
    options.onError?.(error);
  }

  async function runServerTurn(text: string, assistantId: string): Promise<void> {
    abortController = new AbortController();
    const base = (options.apiBaseUrl ?? '').replace(/\/+$/, '');
    try {
      const result = await streamChat({
        url: `${base}/chat/stream`,
        body: {
          message: text,
          threadId,
          agentId: options.agentId ?? null,
          model: options.model ?? null,
        },
        headers: options.headers,
        signal: abortController.signal,
        onDelta: (chunk) => appendDelta(assistantId, chunk),
        onDone: (event) => {
          if (event.threadId) threadId = event.threadId;
        },
      });
      if (destroyed) return;
      if (result.error) {
        reportError(result.error, assistantId);
        return;
      }
      updateMessage(assistantId, { isStreaming: false, status: 'done' });
      setStreaming(false);
    } catch (err) {
      if (destroyed) return;
      reportError(err instanceof Error ? err : new Error(String(err)), assistantId);
    } finally {
      abortController = null;
    }
  }

  function send(text: string): void {
    if (destroyed || !text.trim()) return;

    pushMessage({ role: 'user', content: text });

    if (options.apiBaseUrl) {
      const assistantId = pushMessage({ role: 'assistant', content: '', isStreaming: true });
      setStreaming(true);
      void runServerTurn(text, assistantId);
      return;
    }

    if (!options.onSend) return;
    abortController = new AbortController();
    void Promise.resolve(
      options.onSend(text, {
        pushMessage,
        appendDelta,
        updateMessage,
        setStreaming,
        signal: abortController.signal,
      }),
    ).catch((err: unknown) => {
      if (destroyed) return;
      reportError(err instanceof Error ? err : new Error(String(err)), null);
    });
  }

  function stop(): void {
    abortController?.abort();
    abortController = null;
    messages.value = messages.value.map((m) =>
      m.isStreaming ? { ...m, isStreaming: false, status: 'stopped' } : m,
    );
    setStreaming(false);
  }

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
          onSend: (content: string) => send(content),
          onStop: () => stop(),
        });
    },
  });

  app.mount(mountEl);

  return {
    open() {
      if (destroyed) return;
      isOpen.value = true;
    },
    close() {
      if (destroyed) return;
      isOpen.value = false;
    },
    sendMessage(text: string) {
      send(text);
    },
    stop() {
      if (destroyed) return;
      stop();
    },
    destroy() {
      if (destroyed) return;
      destroyed = true;
      abortController?.abort();
      abortController = null;
      app?.unmount();
      app = null;
      if (ownsMountEl && mountEl.parentNode) {
        mountEl.parentNode.removeChild(mountEl);
      }
    },
  };
}
