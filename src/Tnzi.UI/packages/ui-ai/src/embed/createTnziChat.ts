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
 *   headers: () => ({ Authorization: `Bearer ${auth.accessToken}` }),
 * })
 * chat.open()
 * chat.sendMessage('Hello')
 * chat.destroy()
 * ```
 *
 * **Bring your own transport** - omit `apiBaseUrl` and drive the widget from
 * the outside. `onSend` fires for every user message (with the composer's
 * files as its third argument), and `pushMessage` / `appendDelta` /
 * `setStreaming` feed the reply back in:
 *
 * ```ts
 * const chat = createTnziChat({
 *   mode: 'inline',
 *   el: '#chat',
 *   onSend: async (text, api, files) => {
 *     const id = api.pushMessage({ role: 'assistant', content: '' })
 *     api.setStreaming(true)
 *     for await (const chunk of myStream(text, files)) api.appendDelta(id, chunk)
 *     api.setStreaming(false)
 *   },
 * })
 * ```
 *
 * **Attachments.** The widgets show a paperclip and accept paste / drop by
 * default. Server-backed mode sends images inline and needs `uploadFile` for
 * anything else (a `file` part is a Storage id) - without it a non-image file
 * is refused through `onError`, never silently dropped. A BYO transport that
 * ignores files should pass `enableAttachments: false` so no chip is offered.
 */

import { createApp, ref, h, type App } from 'vue';
import { streamChat } from '@tnzi/core/services/ai';
import {
  attachmentsToContentParts,
  filesToContentParts,
  type ChatContentPart,
  type UploadedAttachment,
} from '../headless/attachment-parts';
import TFloatingChat from './TFloatingChat.vue';
import TSidebarChat from './TSidebarChat.vue';
import TInlineChat from './TInlineChat.vue';
import type { ChatMessage, MessageRole } from '../headless/useChat';

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
  /**
   * Extra request headers (typically `Authorization`). Server-backed mode only.
   *
   * Pass a function to have the headers resolved on every turn. A plain object
   * is a snapshot: an `Authorization` header captured at creation time goes
   * stale the moment the access token rotates, and the stream is a raw fetch
   * that no client refresh path can rescue.
   */
  headers?: Record<string, string> | (() => Record<string, string>);
  /** Agent to route to. Server-backed mode only. */
  agentId?: string | null;
  /** Model override. Server-backed mode only. */
  model?: string | null;
  /** Thread to continue. Server-backed mode only; updated as the server assigns one. */
  threadId?: string | null;
  /** Called for every user message when `apiBaseUrl` is not set. `files` are the composer's attachments. */
  onSend?: (text: string, api: TnziChatTransportApi, files: File[]) => void | Promise<void>;
  /** Called when a request fails. */
  onError?: (error: Error) => void;
  /**
   * Paperclip + drag/drop + paste in the widget. Default true. Turn it off
   * when the transport cannot carry files - a picker whose files go nowhere
   * is worse than none.
   */
  enableAttachments?: boolean;
  /**
   * Server-backed mode: upload a non-image attachment and return its storage
   * id (images travel inline as base64 and need nothing). Without it a
   * non-image file is refused through `onError` and the turn is not sent.
   */
  uploadFile?: (file: File) => Promise<UploadedAttachment>;
  /** Per-attachment ceiling in bytes. Default 10 MB. */
  maxAttachmentBytes?: number;
}

export interface TnziChatInstance {
  /** Open the chat widget. */
  open: () => void;
  /** Close the chat widget. */
  close: () => void;
  /** Send a message programmatically, optionally with attachments. */
  sendMessage: (text: string, files?: File[]) => void;
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

  async function runServerTurn(message: string, parts: ChatContentPart[] | null, assistantId: string): Promise<void> {
    abortController = new AbortController();
    const base = (options.apiBaseUrl ?? '').replace(/\/+$/, '');
    try {
      // `message` stays alongside `content`: the backend builds the model
      // input from the parts when present, but adds and persists the user
      // turn only from a non-blank `message` (a files-only turn sends the
      // attachment names, see `filesToContentParts`).
      const result = await streamChat({
        url: `${base}/chat/stream`,
        body: {
          message,
          threadId,
          agentId: options.agentId ?? null,
          model: options.model ?? null,
          ...(parts ? { content: parts } : {}),
        },
        headers: typeof options.headers === 'function' ? options.headers() : options.headers,
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

  function send(text: string, files: File[] = []): void {
    if (destroyed || (!text.trim() && files.length === 0)) return;

    if (options.apiBaseUrl) {
      void sendServer(text, files);
      return;
    }

    pushMessage({ role: 'user', content: text });
    if (!options.onSend) return;
    abortController = new AbortController();
    void Promise.resolve(
      options.onSend(
        text,
        {
          pushMessage,
          appendDelta,
          updateMessage,
          setStreaming,
          signal: abortController.signal,
        },
        files,
      ),
    ).catch((err: unknown) => {
      if (destroyed) return;
      reportError(err instanceof Error ? err : new Error(String(err)), null);
    });
  }

  /**
   * Attachments are resolved BEFORE the user row appears: a refusal (a file
   * the transport cannot carry, an oversized one, a failed upload) is
   * reported through `onError` and nothing is pushed, instead of a bubble
   * that claims the file was sent.
   */
  async function sendServer(text: string, files: File[]): Promise<void> {
    let resolved: Awaited<ReturnType<typeof filesToContentParts>>;
    try {
      resolved = await filesToContentParts(text, files, {
        uploadFile: options.uploadFile,
        maxBytes: options.maxAttachmentBytes,
      });
    } catch (err) {
      if (!destroyed) options.onError?.(err instanceof Error ? err : new Error(String(err)));
      return;
    }
    if (destroyed) return;
    const { parts, message, attachments } = resolved;
    pushMessage({ role: 'user', content: text, ...(attachments.length ? { attachments } : {}) });
    const assistantId = pushMessage({ role: 'assistant', content: '', isStreaming: true });
    setStreaming(true);
    await runServerTurn(message, parts, assistantId);
  }

  /**
   * Re-ask the question that produced `messageId` (the Retry on an errored
   * row, or the row's regenerate action). The rows from the preceding user
   * turn on are dropped and that turn is sent again, so the list keeps
   * exactly one copy of the question. Server mode rebuilds the parts from
   * the row's attachments; BYO mode hands the text back to `onSend` (its
   * rows carry no attachments).
   */
  function regenerate(messageId: string): void {
    if (destroyed || isStreaming.value) return;
    const idx = messages.value.findIndex((m) => m.id === messageId);
    if (idx === -1 || messages.value[idx]?.role !== 'assistant') return;
    let userIdx = -1;
    for (let i = idx - 1; i >= 0; i--) {
      if (messages.value[i]?.role === 'user') {
        userIdx = i;
        break;
      }
    }
    const userMessage = userIdx === -1 ? null : messages.value[userIdx];
    if (!userMessage) return;

    messages.value = messages.value.slice(0, userIdx);
    if (!options.apiBaseUrl) {
      send(userMessage.content);
      return;
    }
    const attachments = userMessage.attachments ?? [];
    const { parts, message } = attachmentsToContentParts(userMessage.content, attachments);
    pushMessage({ role: 'user', content: userMessage.content, ...(attachments.length ? { attachments } : {}) });
    const assistantId = pushMessage({ role: 'assistant', content: '', isStreaming: true });
    setStreaming(true);
    void runServerTurn(message, parts, assistantId);
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
          enableAttachments: options.enableAttachments ?? true,
          onSend: (content: string, files: File[]) => send(content, files),
          onStop: () => stop(),
          onRegenerate: (messageId: string) => regenerate(messageId),
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
    sendMessage(text: string, files?: File[]) {
      send(text, files);
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
