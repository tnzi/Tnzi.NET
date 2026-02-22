/**
 * @tnzi/shadcn/adapters/message
 *
 * Message adapter implementation for shadcn-vue.
 */

import type { MessageAdapter } from '@tnzi/core/adapters';
import { toast } from 'vue-sonner';

export function createShadcnMessageAdapter(): MessageAdapter {
  return {
    success: (message) => toast.success(message),
    error: (message) => toast.error(message),
    warning: (message) => toast.warning(message),
    info: (message) => toast.info(message),
    loading: (message) => {
      const id = toast.loading(message);
      return () => toast.dismiss(id);
    },
  };
}

export type { MessageAdapter };
