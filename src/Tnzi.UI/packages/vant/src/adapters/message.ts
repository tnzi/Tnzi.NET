/**
 * @tnzi/vant/adapters/message
 *
 * Message adapter implementation for Vant.
 */

import type { MessageAdapter } from '@tnzi/core/adapters';
import { showFailToast, showLoadingToast, showSuccessToast, showToast } from 'vant';

// Re-export Vant functions for direct use in playgrounds
export { showFailToast, showLoadingToast, showSuccessToast, showToast };

export function createVantMessageAdapter(): MessageAdapter {
  return {
    success: (message) => showSuccessToast(message),
    error: (message) => showFailToast(message),
    warning: (message) => showToast({ message, icon: 'warning-o' }),
    info: (message) => showToast(message),
    loading: (message) => {
      const instance = showLoadingToast({
        message,
        forbidClick: true,
        duration: 0,
      });

      return () => instance.close();
    },
  };
}

export type { MessageAdapter };
