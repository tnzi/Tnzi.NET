/**
 * @tnzi/vant/adapters/message
 *
 * Message adapter implementation for Vant.
 */

import type { MessageAdapter, MessageOptions } from '@tnzi/core/adapters';
import { showFailToast, showLoadingToast, showSuccessToast, showToast } from 'vant';

// Re-export Vant functions for direct use in playgrounds
export { showFailToast, showLoadingToast, showSuccessToast, showToast };

function toVantOptions(options?: MessageOptions): Record<string, unknown> | undefined {
  if (!options) return undefined;
  return {
    duration: options.duration,
    closeOnClick: options.closable,
  };
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
