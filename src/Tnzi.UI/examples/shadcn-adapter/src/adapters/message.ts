/**
 * @tnzi/ui/adapters/message
 *
 * Message adapter using built-in Message component (naive-ui style).
 */

import type { MessageAdapter, MessageOptions } from '@tnzi/core/adapters';
import { messageApi } from '../components/primitive/ui/message';

export function createMessageAdapter(): MessageAdapter {
  return {
    success: (message, options?) => {
      messageApi.success(message, {
        duration: options?.duration,
        closable: options?.closable,
      });
    },
    error: (message, options?) => {
      messageApi.error(message, {
        duration: options?.duration,
        closable: options?.closable,
      });
    },
    warning: (message, options?) => {
      messageApi.warning(message, {
        duration: options?.duration,
        closable: options?.closable,
      });
    },
    info: (message, options?) => {
      messageApi.info(message, {
        duration: options?.duration,
        closable: options?.closable,
      });
    },
    loading: (message, options?) => {
      const msg = messageApi.loading(message, {
        duration: options?.duration ?? 0,
        closable: options?.closable,
      });
      return () => msg.destroy();
    },
  };
}

export type { MessageAdapter };
