import type { VNode } from 'vue';

export type MessageType = 'info' | 'success' | 'warning' | 'error' | 'loading';

export interface MessageOptions {
  /** Auto-dismiss duration in ms. 0 = never dismiss. Default: 3000 */
  duration?: number;
  /** Show close button */
  closable?: boolean;
  /** Pause auto-dismiss timer on hover */
  keepAliveOnHover?: boolean;
  /** Custom icon render */
  icon?: () => VNode;
  /** Show icon. Default: true */
  showIcon?: boolean;
  /** Callback when message closes */
  onClose?: () => void;
  /** Callback after leave animation completes */
  onAfterLeave?: () => void;
}

export interface MessageReactive {
  /** Unique ID */
  readonly key: string;
  /** Message content */
  content: string;
  /** Message type */
  type: MessageType;
  /** Close this message */
  destroy: () => void;
}

export interface MessageApi {
  info: (content: string, options?: MessageOptions) => MessageReactive;
  success: (content: string, options?: MessageOptions) => MessageReactive;
  warning: (content: string, options?: MessageOptions) => MessageReactive;
  error: (content: string, options?: MessageOptions) => MessageReactive;
  loading: (content: string, options?: MessageOptions) => MessageReactive;
  destroyAll: () => void;
}

export type MessagePlacement = 'top' | 'top-left' | 'top-right' | 'bottom' | 'bottom-left' | 'bottom-right';
