/**
 * @tnzi/naive-ui/adapters/message
 *
 * Message adapter using Naive UI's message API.
 *
 * Supports two modes:
 * 1. Explicit API injection: pass the useMessage() result directly
 * 2. Global handle: uses window.$message (set by a setup component under NMessageProvider)
 */

import type { MessageAdapter } from '@tnzi/core/adapters';

interface NaiveMessageApi {
  success: (content: string) => void;
  error: (content: string) => void;
  warning: (content: string) => void;
  info: (content: string) => void;
  loading: (content: string) => { destroy: () => void };
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
 * setMessageAdapter(createNaiveMessageAdapter(messageApi));
 * ```
 */
export function createNaiveMessageAdapter(messageApi?: NaiveMessageApi): MessageAdapter {
  return {
    success(content: string) {
      resolveApi(messageApi)?.success(content);
    },
    error(content: string) {
      resolveApi(messageApi)?.error(content);
    },
    warning(content: string) {
      resolveApi(messageApi)?.warning(content);
    },
    info(content: string) {
      resolveApi(messageApi)?.info(content);
    },
    loading(content: string) {
      const instance = resolveApi(messageApi)?.loading(content);
      return () => instance?.destroy();
    },
  };
}
