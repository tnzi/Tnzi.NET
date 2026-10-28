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
 * is refused, never silently dropped: the turn shows as failed in the widget
 * (its Retry sends the same text and files again) and `onError` is called.
 * The framework's default backend does not load file contents (a `file` part
 * gets 501), so only a backend that resolves file references needs `uploadFile`.
 * A BYO transport that ignores files should pass `enableAttachments: false`
 * so no chip is offered.
 *
 * **One turn at a time.** A send while a reply is still streaming (or its
 * attachments are still uploading) is ignored, as the composer's Enter is:
 * two concurrent turns would share the thread id and the abort handle.
 *
 * **Theme.** The widget provides its own naive theme (see `useAiNaiveTheme`):
 * the Tnzi palette rather than naive's stock green, light or dark after
 * `<html>` (a `dark` class or `data-theme="dark"`), which is also what
 * switches the package's `--tnzi-ai-*` colours. Without it the launcher and the send button stayed
 * naive green and light on any page.
 */

import { createApp, ref, h, type App } from 'vue';
import { NConfigProvider } from 'naive-ui';
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
import { useAiNaiveTheme } from '../headless/useAiNaiveTheme';

/** The page's colour mode, read the way the package stylesheet reads it. */
function documentIsDark(): boolean {
  if (typeof document === 'undefined') return false;
  const root = document.documentElement;
  return root.classList.contains('dark') || root.getAttribute('data-theme') === 'dark';
}

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
   * Only for a backend that resolves file references: the default one answers
   * a `file` part with 501.
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
  /**
   * Send a message programmatically, optionally with attachments. Ignored
   * while a reply is streaming.
   */
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
  /**
   * The user row whose question the server reported as stored in the thread (the last one
   * that was). Regenerating that turn asks the server to answer it again (`regenerate`),
   * instead of sending the question as a new message: the thread, and an external agent's
   * session, then lose the old answer rather than gain a second copy of the question.
   */
  let storedUserRowId: string | null = null;
  /**
   * Increments on every server turn AND on every stop, so a turn can tell
   * whether it is still the current one when its awaits return. Without it the
   * tail of a stopped turn (the stream resolves cleanly on abort) re-marked the
   * stopped row `done`, and once a new turn had started it cleared that turn's
   * streaming flag and abort handle, leaving a reply nothing could cancel.
   */
  let turnSeq = 0;
  /**
   * The files of a server turn whose attachments were refused, by the id of
   * its failed row: that row's Retry sends the turn again, and the files are
   * not on any row to rebuild from.
   */
  const refusedTurnFiles = new Map<string, File[]>();

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

  async function runServerTurn(
    turn: number,
    message: string,
    parts: ChatContentPart[] | null,
    assistantId: string,
    userRowId: string,
    regenerateStored = false,
  ): Promise<void> {
    const isCurrent = () => turn === turnSeq;
    const controller = new AbortController();
    abortController = controller;
    const base = (options.apiBaseUrl ?? '').replace(/\/+$/, '');
    try {
      // `message` stays alongside `content`: the backend builds the model
      // input from the parts when present, but adds and persists the user
      // turn only from a non-blank `message` (a files-only turn sends the
      // attachment names, see `filesToContentParts`).
      const result = await streamChat({
        url: `${base}/chat/stream`,
        body: regenerateStored
          ? { threadId, regenerate: true, agentId: options.agentId ?? null, model: options.model ?? null }
          : {
              message,
              threadId,
              agentId: options.agentId ?? null,
              model: options.model ?? null,
              ...(parts ? { content: parts } : {}),
            },
        headers: typeof options.headers === 'function' ? options.headers() : options.headers,
        signal: controller.signal,
        onDelta: (chunk) => {
          if (isCurrent()) appendDelta(assistantId, chunk);
        },
        onEvent: (event) => {
          // An external agent's turn names its stored question in its first event
          if (isCurrent() && event.eventType === 'run_started' && event.userMessageId) storedUserRowId = userRowId;
        },
        onDone: (event) => {
          if (event.threadId) threadId = event.threadId;
          if (isCurrent() && event.userMessageId) storedUserRowId = userRowId;
        },
      });
      // Stopped or superseded: `stop()` already settled this turn's row and the
      // streaming flag, and either may belong to a newer turn by now.
      if (destroyed || !isCurrent()) return;
      if (result.error) {
        reportError(result.error, assistantId);
        return;
      }
      updateMessage(assistantId, { isStreaming: false, status: 'done' });
      setStreaming(false);
    } catch (err) {
      if (destroyed || !isCurrent()) return;
      reportError(err instanceof Error ? err : new Error(String(err)), assistantId);
    } finally {
      if (abortController === controller) abortController = null;
    }
  }

  function send(text: string, files: File[] = []): void {
    if (destroyed || (!text.trim() && files.length === 0)) return;
    if (isStreaming.value) return;

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
   * Attachments are resolved BEFORE the turn's rows appear. Reading and
   * uploading are part of the turn: the Stop button shows, and a second send
   * cannot start underneath it.
   *
   * A refusal (a file the transport cannot carry, an oversized one, a failed
   * upload) is a failed turn like any other: the user row carries the text
   * but no attachments (nothing was attached to the conversation), and the
   * failed row says why. The composer has already cleared its draft and chips,
   * so reporting it only through the optional `onError` lost the message
   * without a word on any page that did not wire one. The row's Retry sends
   * the same text and files again.
   */
  async function sendServer(text: string, files: File[]): Promise<void> {
    const turn = ++turnSeq;
    setStreaming(true);
    let resolved: Awaited<ReturnType<typeof filesToContentParts>>;
    try {
      resolved = await filesToContentParts(text, files, {
        uploadFile: options.uploadFile,
        maxBytes: options.maxAttachmentBytes,
      });
    } catch (err) {
      if (destroyed || turn !== turnSeq) return;
      pushMessage({ role: 'user', content: text });
      const failedId = pushMessage({ role: 'assistant', content: '' });
      refusedTurnFiles.set(failedId, files);
      reportError(err instanceof Error ? err : new Error(String(err)), failedId);
      return;
    }
    // Stopped (or destroyed) while the attachments were being read or uploaded.
    if (destroyed || turn !== turnSeq) return;
    const { parts, message, attachments } = resolved;
    const userRowId = pushMessage({ role: 'user', content: text, ...(attachments.length ? { attachments } : {}) });
    const assistantId = pushMessage({ role: 'assistant', content: '', isStreaming: true });
    await runServerTurn(turn, message, parts, assistantId, userRowId);
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
    // The last turn, whose question the server stored: it can answer it again in place
    const regenerateStored = !!threadId && idx === messages.value.length - 1 && userMessage.id === storedUserRowId;

    const refusedFiles = refusedTurnFiles.get(messageId);
    messages.value = messages.value.slice(0, userIdx);
    // Dropped rows can no longer be retried.
    for (const id of [...refusedTurnFiles.keys()]) {
      if (!messages.value.some((m) => m.id === id)) refusedTurnFiles.delete(id);
    }
    if (!options.apiBaseUrl) {
      send(userMessage.content);
      return;
    }
    if (refusedFiles) {
      void sendServer(userMessage.content, refusedFiles);
      return;
    }
    const attachments = userMessage.attachments ?? [];
    const { parts, message } = attachmentsToContentParts(userMessage.content, attachments);
    const turn = ++turnSeq;
    const userRowId = pushMessage({ role: 'user', content: userMessage.content, ...(attachments.length ? { attachments } : {}) });
    const assistantId = pushMessage({ role: 'assistant', content: '', isStreaming: true });
    setStreaming(true);
    void runServerTurn(turn, message, parts, assistantId, userRowId, regenerateStored);
  }

  function stop(): void {
    turnSeq += 1;
    abortController?.abort();
    abortController = null;
    messages.value = messages.value.map((m) =>
      m.isStreaming ? { ...m, isStreaming: false, status: 'stopped' } : m,
    );
    setStreaming(false);
  }

  const isDark = ref(documentIsDark());
  const themeObserver =
    typeof MutationObserver !== 'undefined' && typeof document !== 'undefined'
      ? new MutationObserver(() => {
          isDark.value = documentIsDark();
        })
      : null;
  themeObserver?.observe(document.documentElement, { attributes: true, attributeFilter: ['class', 'data-theme'] });

  let app: App | null = createApp({
    setup() {
      const { theme, themeOverrides } = useAiNaiveTheme(isDark);
      // `abstract`: no wrapper element around the widget.
      return () =>
        h(NConfigProvider, { abstract: true, theme: theme.value, themeOverrides: themeOverrides.value }, () =>
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
          }),
        );
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
      refusedTurnFiles.clear();
      themeObserver?.disconnect();
      app?.unmount();
      app = null;
      if (ownsMountEl && mountEl.parentNode) {
        mountEl.parentNode.removeChild(mountEl);
      }
    },
  };
}
