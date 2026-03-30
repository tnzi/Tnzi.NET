/**
 * @tnzi/ui/hooks/useShadcnMessage
 *
 * Message hook for shadcn-vue using vue-sonner.
 */

import { toast } from 'vue-sonner';

export function useShadcnMessage() {
  const show = (message: string, type: 'info' | 'success' | 'warning' | 'error' = 'info') => {
    switch (type) {
      case 'success': return toast.success(message);
      case 'error': return toast.error(message);
      case 'warning': return toast.warning(message);
      case 'info':
      default: return toast.info(message);
    }
  };

  return {
    show,
    success: (message: string) => toast.success(message),
    error: (message: string) => toast.error(message),
    warning: (message: string) => toast.warning(message),
    info: (message: string) => toast.info(message),
    loading: (message: string) => { const id = toast.loading(message); return () => toast.dismiss(id); },
  };
}
