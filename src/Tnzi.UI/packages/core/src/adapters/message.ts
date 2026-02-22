/**
 * @tnzi/core/adapters/message
 *
 * Platform-agnostic toast/message adapter.
 */

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

export interface MessageRuntime {
  setAdapter(newAdapter: MessageAdapter): void;
  use(): MessageAdapter;
  reset(): void;
}

export interface MessageRuntimeOptions {
  adapter?: MessageAdapter;
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

function createDefaultMessageAdapter(): MessageAdapter {
  return new ConsoleMessageAdapter();
}

export function createMessageRuntime(options: MessageRuntimeOptions = {}): MessageRuntime {
  const defaultAdapter = createDefaultMessageAdapter();
  let adapter: MessageAdapter = options.adapter ?? defaultAdapter;

  return {
    setAdapter(newAdapter: MessageAdapter) {
      adapter = newAdapter;
    },
    use() {
      return adapter;
    },
    reset() {
      adapter = defaultAdapter;
    },
  };
}

const defaultMessageRuntime = createMessageRuntime();
let activeMessageRuntime: MessageRuntime = defaultMessageRuntime;

export function setActiveMessageRuntime(runtime: MessageRuntime): void {
  activeMessageRuntime = runtime;
}

export function getActiveMessageRuntime(): MessageRuntime {
  return activeMessageRuntime;
}

export function useMessage() {
  return activeMessageRuntime.use();
}

export function setMessageAdapter(newAdapter: MessageAdapter) {
  activeMessageRuntime.setAdapter(newAdapter);
}

export function resetMessageAdapter() {
  activeMessageRuntime.reset();
}
