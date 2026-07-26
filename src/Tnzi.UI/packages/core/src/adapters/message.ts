/**
 * @tnzi/core/adapters/message
 *
 * Platform-agnostic toast/message adapter.
 */

import { createAdapterSingleton } from './singleton';

export interface MessageOptions {
  duration?: number;
  closable?: boolean;
}

export interface MessageAdapter {
  info(content: string, options?: MessageOptions): void;
  success(content: string, options?: MessageOptions): void;
  warning(content: string, options?: MessageOptions): void;
  error(content: string, options?: MessageOptions): void;
  loading(content: string, options?: MessageOptions): () => void;
}

class ConsoleMessageAdapter implements MessageAdapter {
  info(content: string) {
    console.log(`[Info] ${content}`);
  }
  success(content: string) {
    console.log(`[Success] ${content}`);
  }
  warning(content: string) {
    console.warn(`[Warning] ${content}`);
  }
  error(content: string) {
    console.error(`[Error] ${content}`);
  }
  loading(content: string) {
    console.log(`[Loading] ${content}`);
    return () => console.log(`[Loading End] ${content}`);
  }
}

// ============================================
// Singleton
// ============================================

const _slot = createAdapterSingleton<MessageAdapter>('message', () => new ConsoleMessageAdapter());

export function setMessageAdapter(adapter: MessageAdapter): void {
  _slot.set(adapter);
}

export function useMessage(): MessageAdapter {
  return _slot.use();
}

export function resetMessageAdapter(): void {
  _slot.reset();
}
