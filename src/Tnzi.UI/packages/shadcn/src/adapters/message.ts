/**
 * @tnzi/shadcn/adapters/message
 *
 * Message adapter implementation for shadcn-vue.
 */

import type { MessageAdapter, MessageOptions } from '@tnzi/core/adapters';
import { toast } from 'vue-sonner';

function toSonnerOptions(options?: MessageOptions) {
  if (!options) return undefined;
  return {
    duration: options.duration,
    dismissible: options.closable,
  };
}

export function createShadcnMessageAdapter(): MessageAdapter {
  return {
    success: (message, options?) => toast.success(message, toSonnerOptions(options)),
    error: (message, options?) => toast.error(message, toSonnerOptions(options)),
    warning: (message, options?) => toast.warning(message, toSonnerOptions(options)),
    info: (message, options?) => toast.info(message, toSonnerOptions(options)),
    loading: (message, options?) => {
      const id = toast.loading(message, toSonnerOptions(options));
      return () => toast.dismiss(id);
    },
  };
}

export type { MessageAdapter };
