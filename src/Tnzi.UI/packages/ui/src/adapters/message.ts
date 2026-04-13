/**
 * @tnzi/ui/adapters/message
 *
 * Message adapter using Naive UI's message API.
 *
 * Supports two modes:
 * 1. Explicit API injection: pass the useMessage() result directly
 * 2. Global handle: uses window.$message (set by a setup component under NMessageProvider)
 */

import type { MessageAdapter, MessageOptions } from '@tnzi/core/adapters';

interface NaiveMessageApi {
  success: (content: string, options?: Record<string, unknown>) => void;
  error: (content: string, options?: Record<string, unknown>) => void;
  warning: (content: string, options?: Record<string, unknown>) => void;
  info: (content: string, options?: Record<string, unknown>) => void;
  loading: (content: string, options?: Record<string, unknown>) => { destroy: () => void };
}

function toNaiveOptions(options?: MessageOptions): Record<string, unknown> | undefined {
  if (!options) return undefined;
  return {
    duration: options.duration,
    closable: options.closable,
  };
}

/**
 * Resolve the message API: use explicit API if provided, otherwise fallback to window.$message.
 */
function resolveApi(messageApi?: NaiveMessageApi): NaiveMessageApi | undefined {
  if (messageApi) return messageApi;
  return (window as unknown as Record<string, unknown>).$message as NaiveMessageApi | undefined;
}

/**
 * Create a Naive UI message adapter.
 *
 * When called without arguments, the adapter uses window.$message as the global handle.
 * The application must wrap the root with NMessageProvider and expose the API:
 * ```ts
 * // In a setup component under NMessageProvider
 * window.$message = useMessage();
 * ```
 *
 * When called with an explicit API instance (from useMessage() inside setup),
 * it uses that instance directly:
 * ```ts
 * const messageApi = useMessage();
 * setMessageAdapter(createMessageAdapter(messageApi));
 * ```
 */
export function createMessageAdapter(messageApi?: NaiveMessageApi): MessageAdapter {
  return {
    success(content: string, options?: MessageOptions) {
      resolveApi(messageApi)?.success(content, toNaiveOptions(options));
    },
    error(content: string, options?: MessageOptions) {
      resolveApi(messageApi)?.error(content, toNaiveOptions(options));
    },
    warning(content: string, options?: MessageOptions) {
      resolveApi(messageApi)?.warning(content, toNaiveOptions(options));
    },
    info(content: string, options?: MessageOptions) {
      resolveApi(messageApi)?.info(content, toNaiveOptions(options));
    },
    loading(content: string, options?: MessageOptions) {
      const instance = resolveApi(messageApi)?.loading(content, toNaiveOptions(options));
      return () => instance?.destroy();
    },
  };
}
