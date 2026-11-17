/**
 * Which approval queue and message queue a `TChatApp` shows when it is not handed them.
 *
 * Every `useChatThreads` called in a component's setup registers itself (its
 * `messages`, its approval queue and its message queue) with that component, and the component
 * provides the list to its subtree. A component may call it more than once
 * (a product with a chat and a coding agent in one shell keeps two threads
 * instances side by side), so this is a list rather than one value that the
 * second call would overwrite.
 *
 * `TChatApp` picks the instance whose `messages` array is the very array it
 * was given (`:messages="chat.messages.value"`): that is the conversation on
 * screen. When nothing matches by identity (the page passes a mapped or
 * filtered copy) it falls back to the only registered instance; with several
 * and no match it shows no cards and no queue rather than another
 * conversation's. Passing `:approvals` / `:message-queue` always wins and
 * settles it.
 */
import { getCurrentInstance, inject, onScopeDispose, provide, shallowRef, type InjectionKey, type Ref, type ShallowRef } from 'vue';
import type { ApprovalQueue } from './approval-queue';
import type { MessageQueue } from './message-queue';
import type { ChatMessage } from './useChat';

/** One `useChatThreads` instance, as the shell needs to tell it from another. */
export interface ChatThreadsBinding {
  readonly messages: Readonly<Ref<readonly ChatMessage[]>>;
  readonly approvals: ApprovalQueue;
  /** Its queued messages (`useChatThreads().messageQueue`); absent for an instance that keeps none. */
  readonly messageQueue?: MessageQueue;
  /** Stops one task of its conversation's external agent (`useChatThreads().stopTask`). */
  readonly stopTask?: (taskId: string) => Promise<boolean>;
}

/** The instances registered in one component, and the registry of its nearest ancestor that has one. */
export interface ChatThreadsRegistry {
  readonly bindings: Readonly<ShallowRef<readonly ChatThreadsBinding[]>>;
  readonly parent: ChatThreadsRegistry | null;
}

export const CHAT_THREADS_KEY: InjectionKey<ChatThreadsRegistry> = Symbol('tnzi-ai-chat-threads');

type Instance = NonNullable<ReturnType<typeof getCurrentInstance>>;
const registries = new WeakMap<Instance, { registry: ChatThreadsRegistry; list: ShallowRef<readonly ChatThreadsBinding[]> }>();

/**
 * Registers a threads instance with the component being set up, for the
 * `TChatApp` it renders. Outside a component's setup it does nothing.
 * Released when the calling scope ends.
 */
export function provideChatThreads(binding: ChatThreadsBinding): void {
  const instance = getCurrentInstance();
  if (!instance) return;
  let entry = registries.get(instance);
  if (!entry) {
    const list = shallowRef<readonly ChatThreadsBinding[]>([]);
    const registry: ChatThreadsRegistry = { bindings: list, parent: inject(CHAT_THREADS_KEY, null) };
    entry = { registry, list };
    registries.set(instance, entry);
    provide(CHAT_THREADS_KEY, registry);
  }
  const { list } = entry;
  list.value = [...list.value, binding];
  onScopeDispose(() => {
    list.value = list.value.filter((item) => item !== binding);
  });
}

/** Every binding visible from a registry, nearest first. */
function visible(registry: ChatThreadsRegistry | null): ChatThreadsBinding[] {
  const out: ChatThreadsBinding[] = [];
  for (let at = registry; at; at = at.parent) out.push(...at.bindings.value);
  return out;
}

/** The outcome of `pickChatApprovals`: the queue, and whether the choice was ambiguous. */
export interface PickedApprovals {
  readonly approvals: ApprovalQueue | null;
  /** Several instances and none showing these messages: no cards rather than the wrong ones. */
  readonly ambiguous: boolean;
}

/** The threads instance whose messages are `messages`; else the only instance there is; else none. */
function pick(registry: ChatThreadsRegistry | null, messages: readonly unknown[] | undefined): { binding: ChatThreadsBinding | null; ambiguous: boolean } {
  const all = visible(registry);
  const shown = messages ? all.find((binding) => binding.messages.value === messages) : undefined;
  if (shown) return { binding: shown, ambiguous: false };
  if (all.length === 1) return { binding: all[0]!, ambiguous: false };
  return { binding: null, ambiguous: all.length > 1 };
}

/**
 * The approval queue of the threads instance whose messages are `messages`;
 * else the only instance there is; else none (`ambiguous` when there were
 * several to choose from).
 */
export function pickChatApprovals(registry: ChatThreadsRegistry | null, messages: readonly unknown[] | undefined): PickedApprovals {
  const { binding, ambiguous } = pick(registry, messages);
  return { approvals: binding?.approvals ?? null, ambiguous };
}

/** How the instance `pickChatApprovals` picks stops a task; null when none is picked or it cannot. */
export function pickChatStopTask(
  registry: ChatThreadsRegistry | null,
  messages: readonly unknown[] | undefined,
): ((taskId: string) => Promise<boolean>) | null {
  return pick(registry, messages).binding?.stopTask ?? null;
}

/** The message queue of the same instance `pickChatApprovals` picks; null when none is picked or it keeps none. */
export function pickChatMessageQueue(registry: ChatThreadsRegistry | null, messages: readonly unknown[] | undefined): MessageQueue | null {
  return pick(registry, messages).binding?.messageQueue ?? null;
}
