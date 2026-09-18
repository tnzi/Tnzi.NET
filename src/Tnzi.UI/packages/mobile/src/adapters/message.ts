/**
 * @tnzi/mobile/adapters/message
 *
 * Message adapter implementation for Vant.
 */

import type { MessageAdapter, MessageOptions } from '@tnzi/core/adapters';
import { showFailToast, showLoadingToast, showSuccessToast, showToast } from 'vant';

// Re-export Vant functions so consumers can call them directly
export { showFailToast, showLoadingToast, showSuccessToast, showToast };

/**
 * Only the keys the caller actually set. Vant merges toast options with
 * Object.assign, where a key holding `undefined` overrides the default just
 * like a value would - and the Toast prop then falls back to 2000 ms. Spreading
 * `{ duration: undefined }` over `duration: 0` is how a loading toast that
 * promised to stay until closed vanished after two seconds.
 */
function toVantOptions(options?: MessageOptions): Record<string, unknown> {
  const out: Record<string, unknown> = {};
  if (options?.duration !== undefined) out.duration = options.duration;
  if (options?.closable !== undefined) out.closeOnClick = options.closable;
  return out;
}

export function createVantMessageAdapter(): MessageAdapter {
  return {
    success: (message, options?) => showSuccessToast({ message, ...toVantOptions(options) }),
    error: (message, options?) => showFailToast({ message, ...toVantOptions(options) }),
    warning: (message, options?) => showToast({ message, icon: 'warning-o', ...toVantOptions(options) }),
    info: (message, options?) => showToast({ message, ...toVantOptions(options) }),
    loading: (message, options?) => {
      const instance = showLoadingToast({
        message,
        forbidClick: true,
        duration: 0,
        ...toVantOptions(options),
      });

      return () => instance.close();
    },
  };
}

export type { MessageAdapter };
