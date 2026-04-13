/**
 * @tnzi/ui/composables/useMessage
 *
 * Message composable using built-in Message component.
 */

import { messageApi } from '../components/primitive/ui/message';
import type { MessageReactive } from '../components/primitive/ui/message';

export function useMessage() {
  return {
    show: (message: string, type: 'info' | 'success' | 'warning' | 'error' = 'info') => {
      return messageApi[type](message);
    },
    success: (message: string) => messageApi.success(message),
    error: (message: string) => messageApi.error(message),
    warning: (message: string) => messageApi.warning(message),
    info: (message: string) => messageApi.info(message),
    /** Returns MessageReactive with .destroy() method */
    loading: (message: string): MessageReactive => {
      return messageApi.loading(message, { duration: 0 });
    },
    destroyAll: () => messageApi.destroyAll(),
  };
}
